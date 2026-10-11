# Secrets: school-search on Fly.io

Fly §9: non-secret configuration lives in `[env]` in the `flyio/*.fly.toml` files and is reviewed in git.
Everything in this file is a **secret**. Secrets are never committed, never in `[env]`, never in a
shell history, never on a command line, and never printed in a log. They live in exactly three places:

| Place | What lives there | Set by |
|---|---|---|
| GitHub environment `dev` | `FLY_API_TOKEN` (the one root secret) and the deploy inputs listed in section 1 | Human, once |
| Fly app secrets (`fly secrets`) | Everything an app needs at runtime | CI, from the GitHub environment, with `--stage` |
| Local developer machine | Dev values for `dotnet user-secrets` and `.env` (gitignored) | `scripts/setup.sh`, from `secrets.env.example` |

Values are never readable back from Fly: `fly secrets list` shows names and digests only.

## 1. Secret inventory

| Name | Used by | Required? | If absent | Notes |
|---|---|---|---|---|
| `FLY_API_TOKEN` | `flyio.yml`, `flyio-scale.yml`, `flyio-destroy.yml` | Required for any deploy | Deploy fails at the first `flyctl` call | GitHub environment secret, **not** a Fly secret. Created with `fly tokens create org` |
| `POSTGRES_PASSWORD` | `school-search-postgres` (Fly secret) and, assembled into connection strings, `api`, `reviews`, `authservice` | Required | Postgres refuses to initialise; dependent apps fail their deploy | Read only on first boot of an empty volume. Changing it later does not change the database password (use `ALTER USER`) |
| `ConnectionStrings__catalogdb` | `school-search-api-dev` (Fly secret) | Required | API cannot reach its database | Assembled by CI from `POSTGRES_PASSWORD` plus a known host (Fly §9). Host `school-search-postgres.internal`, port 5432, database `catalogdb`, user `schoolsearch` (`POSTGRES_USER` in `flyio.yml`) |
| `ConnectionStrings__reviewsdb` | `school-search-reviews-dev` (Fly secret) | Required from Phase E | Reviews service cannot reach its database | Assembled by CI like the catalog string, database `reviewsdb`. One database per service |
| `ConnectionStrings__DefaultConnection` | `school-search-authservice-dev` (Fly secret) | Required from Phase E | authservice refuses to start | Assembled by CI, database `authservicedb`, same host and user. Owned by the authservice repository's configuration key |
| `Jwt__PrivateKeyPem` | `school-search-authservice-dev` **only** (Fly secret) | Required from Phase E | authservice signs with HS256, its JWKS is empty, and every consumer rejects every token. The post-deploy assertion fails | The **only** signing key in the estate (ADR-0004, P5). Set from the GitHub secret `AUTHSERVICE_JWT_PRIVATE_KEY_PEM`. See section 3 and `docs/runbooks/AUTHSERVICE.md` |
| `Reviews__SubjectPepper` | `school-search-reviews-dev` (Fly secret) | Required from Phase E | The Reviews service does not start in Production | Keys every stored author hash (`SHA-256(pepper + sub)`). **Never rotate it once reviews exist**: rotation breaks the one-review-per-author rule and every existing hash. Generate once: `openssl rand -hex 32` |
| `SESSION_SECRET` | `school-search-web-dev` (Fly secret) | Required from Phase E | The BFF cannot seal its session cookie; account routes answer 503 | AES-256-GCM key for the `ss_session` cookie, at least 32 bytes. Rotating it signs every user out. Generate: `openssl rand -base64 48` |
| `BFF_SHARED_SECRET` | `school-search-api-dev`, `school-search-reviews-dev`, `school-search-web-dev` (same value, Fly secret) | Optional until Phase C is live, then required | Without it every visitor shares one rate-limit bucket (the API and Reviews cannot trust a forwarded client address) | The web BFF sends the visitor IP plus this secret; the services honour the forwarded IP only on a constant-time match. Generate once: `openssl rand -base64 32`. The deploy job sets it only when the GitHub environment has it |
| `ADMIN_API_KEY` | (removed) | n/a | n/a | **Removed in Phase E** (ADR-0015). Admin access is a validated authservice token with role `Admin` or `SuperAdmin`. Delete the Fly secret if it still exists: `fly secrets unset -a school-search-api-dev ADMIN_API_KEY` |
| `MCP_API_KEY` | `school-search-mcp-dev` (Fly secret) | Optional | MCP calls the catalog without a key, so its requests share the anonymous IP bucket | The MCP server's own key for the Api's free-key bucket, sent as `X-Api-Key`. Not a user key. The MCP endpoint itself has no auth in v1 |
| `ANTHROPIC_API_KEY` | `school-search-api-dev` and, from Phase E when triage is on, `school-search-reviews-dev` (Fly secret) | Optional | The explain endpoint answers 503 `ai_unavailable`; triage stays idle (P8) | Needed only with `AI_ENABLED=true`. `flyio.yml` does not push it yet: set it by hand with `flyctl secrets import --stage` (section 2) until the workflow carries it |
| `AI_ENABLED` | `[env]` in `flyio/api.fly.toml` (not set) and `flyio/reviews.fly.toml` (`false`) | Optional, **not a secret** | `false`: AI features are off | Feature switch. Public value, so it belongs in `[env]`, not in Fly secrets |
| `AI_DAILY_TOKEN_BUDGET` | `school-search-api-dev` (`[env]`, not set) | Optional, **not a secret** | Code default `200000` total tokens per UTC day | Cost guard for the explain endpoint (ADR-0011) |
| `AUTHSERVICE_JWT_PRIVATE_KEY_PEM` | GitHub environment `dev` only | Required for the authservice deploy | The authservice deploy job fails before it changes anything | One line with literal `\n` escapes for newlines (authservice normalises them). Never printed. Imported into Fly as `Jwt__PrivateKeyPem` |
| `AUTHSERVICE_IMAGE` | GitHub environment `dev`, repository variable | Required for the authservice deploy | The authservice deploy job fails before it changes anything | Not a secret. The pinned tag, for example `ghcr.io/konradcinkusz/authservice:v0.3.4`. Never `:latest` |
| `AUTHSERVICE_ENABLED` | GitHub environment `dev`, repository variable | Optional, **not a secret** | `false`: the authservice deploy is skipped | Set to `true` only when the authservice app, database and key exist |
| `SESSION_SECRET`, `REVIEWS_SUBJECT_PEPPER`, `BFF_SHARED_SECRET`, `AUTHSERVICE_JWT_PRIVATE_KEY_PEM` | GitHub environment `dev` (inputs to CI) | See rows above | See rows above | Stored in GitHub only as the inputs named here. Fly holds the derived Fly secret names above |
| GHCR pull credential (`GHCR_USER`, `GHCR_TOKEN`) | Every app that pulls a private GHCR image (Fly secrets) | Required while the GHCR packages are private | Image pull fails on deploy | See section 3. Not needed if the packages are made public |

Not secrets (public, in `[env]`): `CATALOG_API_URL`, `DATABASE_PROVIDER`, `REVIEWS_BASE_URL`, `AUTH_BASE_URL`,
`Auth__Authority`, `Auth__Issuer`, `Auth__Audience`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__PublicBaseUrl`,
`Dsa__ContactEmail` (owner-supplied, left unset until given), `POSTGRES_USER`, `POSTGRES_DB`, `PGDATA`,
`ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`, `NODE_ENV`, `PORT`, `HOSTNAME`.

Not needed at all: CI's push to GHCR uses the workflow's built-in `GITHUB_TOKEN` with `packages: write`.
No stored credential is required for that step.

## 2. How to set a secret

Use `--stage` so the change is held until the next deploy. Setting a secret without `--stage` restarts the app,
and one release should not restart a service twice.

**Never put a value on a command line.** A value in argv is visible in `ps` output and in shell history. Use stdin:

From CI (the normal path). The value comes from a GitHub environment secret and reaches flyctl on stdin:

```bash
printf 'ConnectionStrings__catalogdb=%s\n' "$CONNECTION_STRING" \
  | flyctl secrets import --stage --app school-search-api-dev
```

`flyio.yml` builds each connection string from `POSTGRES_PASSWORD` in the same step. The connection string is never
stored in GitHub. A multi-line PEM is not sent as one value: the GitHub secret holds it on one line with literal
`\n` escapes, and `printf '%s'` passes it through unchanged.

Manual, from a trusted terminal (the value is typed or read from a file, never put in the command line):

```bash
# Read the value without echoing it; it stays out of shell history.
read -r -s -p "value: " V; echo
printf 'BFF_SHARED_SECRET=%s\n' "$V" | flyctl secrets import --stage --app school-search-api-dev
unset V
flyctl secrets list --app school-search-api-dev      # names and digests only
```

A missing critical secret fails the deploy. The workflow checks for `ConnectionStrings__catalogdb`,
`ConnectionStrings__reviewsdb`, `Reviews__SubjectPepper`, `Jwt__PrivateKeyPem`, `SESSION_SECRET` and
`POSTGRES_PASSWORD` and exits non-zero when one is missing. A service that boots without its database credential or
its key would look healthy while doing nothing useful (Fly §9).

## 3. One-time human setup (per repository)

Nothing else is created by hand, except the databases below. No `fly launch`, no `fly apps create`, no volumes.

1. **Deploy token.** Run `fly tokens create org` (org: personal or the owning org). Store the output as the
   secret `FLY_API_TOKEN` in the GitHub environment `dev`. Use an environment, not a repository secret, so
   it can be reviewed and restricted.
2. **GHCR pull credential.** Create a fine-grained GitHub token with `read:packages` on
   `konradcinkusz`. Store it on each app that pulls a private image. Usernames and tokens go in as two secrets:
   `flyctl secrets import --stage --app <app>` with `GHCR_USER=...` and `GHCR_TOKEN=...` on stdin.
   Open point: how Fly's remote builder consumes this pair for a private `--image` is not verified in this
   repository. If that cannot be made to work, make the GHCR packages public instead. Then no credential exists.
3. **Phase E databases.** On `school-search-postgres` (one instance, three logical databases, one per service),
   create `reviewsdb` and `authservicedb` once. Steps are in `docs/runbooks/AUTHSERVICE.md` section 2.
4. **Phase E keys and values.** Generate and store as GitHub `dev` environment secrets: `REVIEWS_SUBJECT_PEPPER`
   (`openssl rand -hex 32`), `SESSION_SECRET` (`openssl rand -base64 48`), `AUTHSERVICE_JWT_PRIVATE_KEY_PEM`
   (generated once, one line with `\n` escapes, runbook section 1), optionally `BFF_SHARED_SECRET`. Repository
   variables: `AUTHSERVICE_IMAGE` (the pinned tag) and `AUTHSERVICE_ENABLED=true` when the authservice app is wanted.
5. **Optional keys.** Set `MCP_API_KEY` and `ANTHROPIC_API_KEY` only when the feature that needs them is enabled.
   Each can be skipped until then (P8), and the `/health` endpoint shows which integrations are degraded.

## 4. Deploy order and commands (reference, not executed by agents)

Build once in CI and deploy the same image tag everywhere. Never build inside a deploy step (Fly §10).
Agents never run these commands. Deploys come from a `v*` tag in CI.

```bash
flyctl deploy --config flyio/postgres.fly.toml --image postgres:17-alpine --ha=false
flyctl deploy --config flyio/authservice.fly.toml --image ghcr.io/konradcinkusz/authservice:v0.3.4   # Phase E, pinned tag
flyctl deploy --config flyio/reviews.fly.toml  --image ghcr.io/konradcinkusz/school-search-reviews:<tag>   # Phase E
flyctl deploy --config flyio/api.fly.toml      --image ghcr.io/konradcinkusz/school-search-api:<tag>
flyctl deploy --config flyio/mcp.fly.toml      --image ghcr.io/konradcinkusz/school-search-mcp:<tag>
flyctl deploy --config flyio/web.fly.toml      --image ghcr.io/konradcinkusz/school-search-web:<tag>
```

Order: postgres, authservice, reviews, api, mcp, web. Gate postgres separately, because redeploying it restarts it.
Post-deploy checks for authservice are in `scripts/assert-authservice.sh`; the workflow runs them after its deploy.
