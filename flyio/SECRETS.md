# Secrets: school-search on Fly.io

Fly §9: non-secret configuration lives in `[env]` in the `flyio/*.fly.toml` files and is reviewed in git.
Everything in this file is a **secret**. Secrets are never committed, never in `[env]`, never in a
shell history, and never printed in a log. They live in exactly three places:

| Place | What lives there | Set by |
|---|---|---|
| GitHub environment `dev` | `FLY_API_TOKEN` only (the one root secret) | Human, once |
| Fly app secrets (`fly secrets`) | Everything an app needs at runtime | CI, from the GitHub environment, with `--stage` |
| Local developer machine | Dev values for `dotnet user-secrets` and `.env` (gitignored) | `scripts/setup.sh`, from `secrets.env.example` (A1) |

Values are never readable back from Fly: `fly secrets list` shows names and digests only.

## 1. Secret inventory

| Name | Used by | Required? | If absent | Notes |
|---|---|---|---|---|
| `FLY_API_TOKEN` | `flyio.yml`, `flyio-scale.yml`, `flyio-destroy.yml` | Required for any deploy | Deploy fails at the first `flyctl` call | GitHub environment secret, **not** a Fly secret. Created with `fly tokens create org` |
| `POSTGRES_PASSWORD` | `school-search-postgres` (Fly secret) | Required | Postgres refuses to initialise | Read only on first boot of an empty volume. Changing it later does not change the database password (use `ALTER USER`) |
| `ConnectionStrings__catalogdb` | `school-search-api-dev` (Fly secret) | Required | API cannot reach its database (InMemory fallback only when `DATABASE_PROVIDER` is unset, which is not production) | Assembled by CI from `POSTGRES_PASSWORD` plus a known host, never stored per service (Fly §9). Host `school-search-postgres.internal`, port 5432, database `catalogdb`, user `schoolsearch`. The `.internal` host resolves to IPv6 only, so the driver must be IPv6-capable |
| `ADMIN_API_KEY` | `school-search-api-dev` (Fly secret) | Optional now, needed from Phase C | Admin endpoints stay disabled (P8) | Protects admin and correction-path endpoints. Phase C+ only |
| `MCP_API_KEY` | `school-search-mcp-dev` (Fly secret) | Optional now, needed when MCP is live | MCP serves anonymous (IP bucket) traffic only | The MCP server holds its own key for the free API-key rate bucket (MASTER-PROMPT, rate limiting) |
| `ANTHROPIC_API_KEY` | `school-search-api-dev` (Fly secret) | Optional | AI features report `degraded` on `/health` and do nothing (P8) | Only needed when an AI feature is enabled |
| GHCR pull credential (`GHCR_USER`, `GHCR_TOKEN`) | Every app that pulls a private GHCR image (Fly secrets) | Required while the GHCR packages are private | Image pull fails on deploy | See section 3. Not needed if the packages are made public |
| Authservice signing key (name set in Phase E) | `school-search-authservice-dev` only (Fly secret) | Phase E | n/a in Phase A-D | The only signing key in the estate (P5). Its name and format come from the authservice repo; not defined here. Every other app validates against JWKS and holds no key |

Not secrets (public, in `[env]`): `CATALOG_API_URL`, `DATABASE_PROVIDER`, `POSTGRES_USER`,
`POSTGRES_DB`, `PGDATA`, `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`, `NODE_ENV`, `PORT`, `HOSTNAME`.

Not needed at all: CI's push to GHCR uses the workflow's built-in `GITHUB_TOKEN` with `packages: write`.
No stored credential is required for that step.

## 2. How to set a secret

Use `--stage` so the change is held until the next deploy. Setting a secret without `--stage` restarts the app,
and one release should not restart a service twice.

Manual, from a trusted terminal (the value is typed or read from a file, never put in the command line):

```bash
# Read the value without echoing it; it stays out of shell history.
read -r -s -p "value: " V; echo
fly secrets set --stage -a school-search-api-dev "ADMIN_API_KEY=$V"
unset V
fly secrets list -a school-search-api-dev      # names and digests only
```

From CI (the normal path). The value comes from a GitHub environment secret and reaches flyctl on stdin,
so it never appears in `ps` output or in a log:

```bash
printf 'ConnectionStrings__catalogdb=%s\n' "$CONNECTION_STRING" \
  | flyctl secrets import --stage -a school-search-api-dev
```

`CONNECTION_STRING` is assembled from the `POSTGRES_PASSWORD` environment secret. It is never stored in GitHub.
Masking is on for every secret value in Actions.

A missing critical secret fails the deploy. The workflow checks for `ConnectionStrings__catalogdb` (and
`POSTGRES_PASSWORD` for the database app) and exits non-zero. A service that boots without its database
credential would look healthy while doing nothing useful (Fly §9).

## 3. One-time human setup (per repository)

Three steps. Nothing else is created by hand: no `fly launch`, no `fly apps create`, no volumes.

1. **Deploy token.** Run `fly tokens create org` (org: personal or the owning org). Store the output as the
   secret `FLY_API_TOKEN` in the GitHub environment `dev`. Use an environment, not a repository secret, so
   it can be reviewed and restricted.
2. **GHCR pull credential.** Create a fine-grained GitHub token with `read:packages` on
   `konradcinkusz`. Store it on each app that pulls a private image. Usernames and tokens go in as two secrets:
   `fly secrets set --stage -a <app> GHCR_USER=... GHCR_TOKEN=...`
   Open point: how Fly's remote builder consumes this pair for a private `--image` is not verified in this
   repository. If that cannot be made to work, make the GHCR packages public instead. Then no credential exists.
3. **Optional keys.** Set `ADMIN_API_KEY`, `MCP_API_KEY` and `ANTHROPIC_API_KEY` only when the feature that
   needs them is enabled. Each one can be skipped until then (P8), and the `/health` endpoint shows which
   integrations are degraded.

## 4. Deploy commands (reference, not executed by agents)

Build once in CI and deploy the same image tag everywhere. Never build inside a deploy step (Fly §10).

```bash
flyctl deploy --config flyio/postgres.fly.toml --image postgres:17-alpine --ha=false
flyctl deploy --config flyio/api.fly.toml      --image ghcr.io/konradcinkusz/school-search-api:<tag>
flyctl deploy --config flyio/web.fly.toml      --image ghcr.io/konradcinkusz/school-search-web:<tag>
flyctl deploy --config flyio/mcp.fly.toml      --image ghcr.io/konradcinkusz/school-search-mcp:<tag>      # Mcp phase
flyctl deploy --config flyio/authservice.fly.toml --image ghcr.io/konradcinkusz/authservice:<tag>         # Phase E
```

Order: postgres, authservice, api, mcp, web (Fly §10). Gate postgres separately, because redeploying it restarts it.
