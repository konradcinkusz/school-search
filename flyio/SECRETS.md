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
| `POSTGRES_PASSWORD` | `school-search-postgres` (Fly secret) and, assembled into connection strings, `api`, `reviews`, `billing`, `authservice` | Required | Postgres refuses to initialise; dependent apps fail their deploy | Read only on first boot of an empty volume. Changing it later does not change the database password (use `ALTER USER`) |
| `ConnectionStrings__catalogdb` | `school-search-api-dev` (Fly secret) | Required | API cannot reach its database | Assembled by CI from `POSTGRES_PASSWORD` plus a known host (Fly §9). Host `school-search-postgres.internal`, port 5432, database `catalogdb`, user `schoolsearch` (`POSTGRES_USER` in `flyio.yml`) |
| `ConnectionStrings__reviewsdb` | `school-search-reviews-dev` (Fly secret) | Required from Phase E | Reviews service cannot reach its database | Assembled by CI like the catalog string, database `reviewsdb`. One database per service |
| `ConnectionStrings__DefaultConnection` | `school-search-authservice-dev` (Fly secret) | Required from Phase E | authservice refuses to start | Assembled by CI, database `authservicedb`, same host and user. Owned by the authservice repository's configuration key |
| `Jwt__PrivateKeyPem` | `school-search-authservice-dev` **only** (Fly secret) | Required from Phase E | authservice signs with HS256, its JWKS is empty, and every consumer rejects every token. The post-deploy assertion fails | The **only** signing key in the estate (ADR-0004, P5). Set from the GitHub secret `AUTHSERVICE_JWT_PRIVATE_KEY_PEM`. See section 3 and `docs/runbooks/AUTHSERVICE.md` |
| `Reviews__SubjectPepper` | `school-search-reviews-dev` (Fly secret) | Required from Phase E | Review writes answer 503 in Production (reads still work). The deploy job refuses to run without the GitHub secret | Keys every stored author hash (`SHA-256(pepper + sub)`). **Never rotate it once reviews exist**: rotation breaks the one-review-per-author rule and every existing hash. Generate once: `openssl rand -hex 32` |
| `ConnectionStrings__billingdb` | `school-search-billing-dev` (Fly secret) | Required from Phase F | Billing service cannot reach its database | Assembled by CI like the reviews string, database `billingdb`, same host and user. One database per service. Created once by hand (`docs/runbooks/BILLING.md` section 2) |
| `Billing__SubjectPepper` | `school-search-billing-dev` (Fly secret) | Required from Phase F | The billing deploy job refuses to run without the GitHub secret `BILLING_SUBJECT_PEPPER` | Keys the subject hash of claims and subscriptions (`SHA-256(pepper + sub)`, the Reviews rule). **Never rotate it once claims or subscriptions exist.** Generate once: `openssl rand -hex 32`. Unrelated to `Reviews__SubjectPepper`: never reuse one value for both |
| `Billing__WebhookSecret` | `school-search-billing-dev` (Fly secret) | Optional until a payment provider is chosen; required before any live webhook | Without it, in Production every webhook is rejected with 401 (correct, never accept unsigned, MONETIZATION-BRIEF 1d). The deploy warns and continues | The HMAC key the provider signs webhooks with. It comes from the provider's dashboard once the provider is chosen (ADR-0016), so it is not generated here. GitHub secret `BILLING_WEBHOOK_SECRET`. Confirm the signature header name against real traffic before go-live (`docs/runbooks/BILLING.md` section 4) |
| `SESSION_SECRET` | `school-search-web-dev` (Fly secret) | Required from Phase E | The BFF cannot seal its session cookie; account routes answer 503. The deploy job refuses a missing value or one shorter than 32 characters | AES-256-GCM key for the `ss_session` cookie, at least 32 bytes. Rotating it signs every user out. Generate: `openssl rand -base64 48` |
| `GOOGLE_MAPS_API_KEY` | `school-search-web-dev` (Fly secret, **runtime**, never `NEXT_PUBLIC_*`) | Optional | `/mapa` renders its list fallback and the CSP does not allow Google hosts (P8, `docs/adr/0022-map-search-google-maps.md`) | GitHub environment secret of the same name. A browser key is public by nature (it reaches the page through `/api/config`), so the protection is on Google's side: restrict it by **HTTP referrer** to the site origin(s) and to the Maps JavaScript API (and Geocoding API if used). Never reuse a server key. The deploy job pushes it only when set |
| `BFF_SHARED_SECRET` | `school-search-web-dev`, `school-search-api-dev`, `school-search-reviews-dev`, `school-search-billing-dev` (**the same value**, Fly secret) and, optional, `school-search-mcp-dev` | **Required** by the api, reviews, billing and web deploy jobs (they refuse to run without it). Optional for mcp | Without it every visitor shares one rate-limit bucket (the services cannot trust a forwarded client address) | The web BFF sends the visitor IP plus this secret; the services honour the forwarded IP only on a constant-time match. mcp sends it towards Reviews so the visitor, not the MCP service, is metered. One GitHub secret feeds every app, so the copies cannot diverge. Generate once: `openssl rand -base64 32` |
| `ADMIN_API_KEY` | (removed) | n/a | n/a | **Removed in Phase E** (ADR-0015). Admin access is a validated authservice token with role `Admin` or `SuperAdmin`. Delete the Fly secret if it still exists: `fly secrets unset -a school-search-api-dev ADMIN_API_KEY` |
| `MCP_API_KEY` | `school-search-mcp-dev` (Fly secret) | Optional | MCP calls the catalog without a key, so its requests share the anonymous IP bucket | The MCP server's own key for the Api's free-key bucket, sent as `X-Api-Key`. Not a user key. The MCP endpoint itself has no auth in v1 |
| `ANTHROPIC_API_KEY` | `school-search-api-dev`, `school-search-reviews-dev`, `school-search-billing-dev` and `school-search-mcp-dev` (Fly secrets, one copy each) | Optional | Each feature answers 503 `ai_unavailable` (the explain and search-intent endpoints, the profile assistant, the agent `POST /agent/chat`); triage stays idle; `/health` lists `anthropic` as disabled (P8) | GitHub environment secret `ANTHROPIC_API_KEY`. `flyio.yml` pushes it with `flyctl secrets import --stage` on stdin to the four apps above, only when the GitHub secret is set, and never echoes it (Phase G). Pushing it enables nothing: each app's `AI_ENABLED` kill switch in `flyio/*.fly.toml` stays `false` until the eval gate passes (ADR-0011, ADR-0017, `docs/runbooks/AI-OPERATIONS.md`). Each service holds its own copy, so the key is never shared between services by a file |
| `AI_ENABLED` | `[env]` in `flyio/api.fly.toml`, `flyio/reviews.fly.toml`, `flyio/billing.fly.toml` and `flyio/mcp.fly.toml` (all `false`; `flyio/check-config.sh` fails the `test` job when a line is missing, because the code default is ON in api, reviews and billing) | Optional, **not a secret** | `false`: AI features are off, whatever the key says | Kill switch, one per service. A public value, so it belongs in `[env]`, not in Fly secrets. Flip to `true` only as a reviewed change after the open items and the eval gate of that feature (`docs/ai/AI-FEATURES.md`, `docs/runbooks/AI-OPERATIONS.md`). Setting `AI_ENABLED=false` (or removing the key) is the kill switch in an incident |
| `AI_DAILY_TOKEN_BUDGET` | `[env]` in `flyio/api.fly.toml` (`200000`), `flyio/mcp.fly.toml` (`100000`, the agent) and the code default in reviews and billing (`100000`) | Optional, **not a secret** | Code default: `200000` in the Api, `100000` elsewhere, total tokens per UTC day | Cost guard, in memory per process (ADR-0011 item 11, ADR-0017). A restart or a second instance gets its own counter, so it is a soft cap until the counter moves to Postgres |
| `AUTHSERVICE_JWT_PRIVATE_KEY_PEM` | GitHub environment `dev` only | Required for the authservice deploy | The authservice deploy job fails before it changes anything | One line with literal `\n` escapes for newlines (authservice normalises them). Never printed. Imported into Fly as `Jwt__PrivateKeyPem` |
| `AUTHSERVICE_IMAGE` | GitHub environment `dev`, repository variable | Required for the authservice deploy | The authservice deploy job fails before it changes anything | Not a secret. The pinned tag, for example `ghcr.io/konradcinkusz/authservice:v0.3.4`. Never `:latest` |
| `AUTHSERVICE_ENABLED` | GitHub environment `dev`, repository variable | Optional, **not a secret** | `false`: the authservice deploy is skipped | Set to `true` only when the authservice app, database and key exist |
| `SESSION_SECRET`, `REVIEWS_SUBJECT_PEPPER`, `BILLING_SUBJECT_PEPPER`, `BFF_SHARED_SECRET`, `AUTHSERVICE_JWT_PRIVATE_KEY_PEM`, `GOOGLE_MAPS_API_KEY` | GitHub environment `dev` (inputs to CI) | See rows above | See rows above | Stored in GitHub only as the inputs named here. Fly holds the derived Fly secret names above |
| GHCR visibility | The images `school-search-{api,reviews,billing,mcp,web}` and `authservice` | One of the two options is required | Image pull fails on deploy | **Not a Fly secret.** The earlier `GHCR_USER` / `GHCR_TOKEN` Fly-secret idea is not implemented and is not a documented way for Fly to pull a private image. Choose (a) make the packages public, or (b) set the repository variable `FLY_REGISTRY_MIRROR=true` so CI copies each image to `registry.fly.io` (needs the optional secret `GHCR_PULL_TOKEN` for the authservice image only while it is private). `flyio/ghcr-probe.sh` warns early. See section 3 |
| `GHCR_PULL_TOKEN`, `GHCR_USER` | GitHub environment `dev` (secret) and repository variable | Optional; only with `FLY_REGISTRY_MIRROR=true` and a private authservice package | The mirror cannot pull a package that belongs to another repository | A token with `read:packages`. Used only by `docker login` on the runner; never sent to Fly |
| `FLY_REGISTRY_MIRROR`, `SKIP_DB_BOOTSTRAP` | Repository variables | Optional, **not secrets** | Defaults: images are pulled from GHCR by Fly; the `ensure_databases` job runs | `FLY_REGISTRY_MIRROR=true` mirrors images to `registry.fly.io`. `SKIP_DB_BOOTSTRAP=true` skips the database creation job when the databases were created by hand |

Not secrets (public, in `[env]`): `CATALOG_API_URL`, `DATABASE_PROVIDER`, `REVIEWS_BASE_URL`, `AUTH_BASE_URL`,
`Auth__Authority`, `Auth__Issuer`, `Auth__Audience`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__PublicBaseUrl`,
`Billing__MockMode` (`false` in `flyio/billing.fly.toml`), `BILLING_BASE_URL` (web), `AGENT_BASE_URL` (web, public MCP URL for `/doradca`), `Dsa__ContactEmail` (owner-supplied, left unset until given), `POSTGRES_USER`, `POSTGRES_DB`, `PGDATA`,
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

Never set on any Fly app: `DevSeed__Enabled`, `DevSeed__Key`, `SCHOOLSEARCH_DEMO_SEED` (the Production guard refuses them, and
`flyio/check-config.sh` fails the `test` job if a fly.toml sets one). `SCHOOLSEARCH_DEMO_SEED` is allowed only on a separate,
non-Production demo environment (`docs/runbooks/FLY-DEPLOY.md` section 11).

A missing critical secret fails the deploy. The workflow checks for `ConnectionStrings__catalogdb`,
`ConnectionStrings__reviewsdb`, `Reviews__SubjectPepper`, `ConnectionStrings__billingdb`, `Billing__SubjectPepper`,
`Jwt__PrivateKeyPem`, `SESSION_SECRET` (and its length), `BFF_SHARED_SECRET` and `POSTGRES_PASSWORD` and exits non-zero when one is missing;
it also refuses a billing pepper equal to the reviews pepper. A service that boots without its database credential or
its key would look healthy while doing nothing useful (Fly §9).

## 3. One-time human setup (per repository)

Nothing is created by hand. No `fly launch`, no `fly apps create`, no volumes.

1. **Deploy token.** Run `fly tokens create org` (org: personal or the owning org). Store the output as the
   secret `FLY_API_TOKEN` in the GitHub environment `dev`. Use an environment, not a repository secret, so
   it can be reviewed and restricted.
2. **GHCR visibility.** Fly pulls `--image ghcr.io/...` without any credential from this repository. Either make the
   packages public (nothing secret is in the images; `.dockerignore` keeps secrets and docs out), or set the repository
   variable `FLY_REGISTRY_MIRROR=true` and let CI copy each image to `registry.fly.io`. Whether Fly can pull a private GHCR
   image with a Fly-secret credential is **not verified** and is not implemented. See `docs/runbooks/FLY-DEPLOY.md` section 2.
3. **Databases.** Created by the `ensure_databases` job of `flyio.yml` (`catalogdb` by the image, then `reviewsdb`,
   `billingdb`, `authservicedb`), idempotently. The job uses `flyctl ssh console` and has not been run: if it fails, create
   them by hand (`docs/runbooks/AUTHSERVICE.md` section 2, `docs/runbooks/BILLING.md` section 2) and set `SKIP_DB_BOOTSTRAP=true`.
4. **Phase E keys and values.** Generate and store as GitHub `dev` environment secrets: `REVIEWS_SUBJECT_PEPPER`
   (`openssl rand -hex 32`), `SESSION_SECRET` (`openssl rand -base64 48`), `AUTHSERVICE_JWT_PRIVATE_KEY_PEM`
   (generated once, one line with `\n` escapes, runbook section 1), `BFF_SHARED_SECRET` (`openssl rand -base64 32`, required). Repository
   variables: `AUTHSERVICE_IMAGE` (the pinned tag) and `AUTHSERVICE_ENABLED=true` when the authservice app is wanted.
5. **Phase F keys.** Generate and store as GitHub `dev` environment secrets: `BILLING_SUBJECT_PEPPER`
   (`openssl rand -hex 32`, a value different from `REVIEWS_SUBJECT_PEPPER`). The deploy job for the billing service
   refuses to run without it. Optionally `BILLING_WEBHOOK_SECRET` once a payment provider is chosen (runbook
   `docs/runbooks/BILLING.md` section 1 and section 4). Nothing else is needed before the first deploy.
6. **Optional keys.** Set `MCP_API_KEY` and `ANTHROPIC_API_KEY` only when the feature that needs them is enabled. For
   `ANTHROPIC_API_KEY` store the GitHub `dev` environment secret of that name. CI then pushes it to the four apps that use it
   (api, reviews, billing, mcp). A key pushed this way still leaves every `AI_ENABLED` switch at `false`.
   Each can be skipped until then (P8), and the `/health` endpoint shows which integrations are degraded.

## 4. Deploy order and commands (reference, not executed by agents)

The pipeline is `.github/workflows/flyio.yml`; the owner's step-by-step is `docs/runbooks/FLY-DEPLOY.md`. The commands below
are what each job runs, for reference only.

Build once in CI and deploy the same image tag everywhere. Never build inside a deploy step (Fly §10).
Agents never run these commands. Deploys come from a `v*` tag in CI.

```bash
flyctl deploy --config flyio/postgres.fly.toml --image postgres:17-alpine --ha=false
flyctl deploy --config flyio/authservice.fly.toml --image ghcr.io/konradcinkusz/authservice:v0.3.4   # Phase E, pinned tag
flyctl deploy --config flyio/api.fly.toml      --image ghcr.io/konradcinkusz/school-search-api:<tag>
flyctl deploy --config flyio/reviews.fly.toml  --image ghcr.io/konradcinkusz/school-search-reviews:<tag>   # Phase E
flyctl deploy --config flyio/billing.fly.toml  --image ghcr.io/konradcinkusz/school-search-billing:<tag>   # Phase F
flyctl deploy --config flyio/mcp.fly.toml      --image ghcr.io/konradcinkusz/school-search-mcp:<tag>
flyctl deploy --config flyio/web.fly.toml      --image ghcr.io/konradcinkusz/school-search-web:<tag>
```

Order: postgres, databases, authservice, api, reviews, billing, mcp, web. Gate postgres separately, because redeploying it restarts it.
Every job checks all its upstream results (success or skipped). Billing needs authservice (token validation); web goes last
because the BFF calls everything else.
Post-deploy checks for authservice are in `scripts/assert-authservice.sh`; the workflow runs them after its deploy.
