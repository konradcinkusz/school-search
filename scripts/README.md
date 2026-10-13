# Scripts

Operational and onboarding scripts for school-search. Each script runs on its own;
none depends on another being run first except where a step says so.

| Script | What it does | Runs |
|---|---|---|
| `setup.sh` / `setup.ps1` | One-command onboarding in numbered steps. `--check` / `-Check` lists missing tools by name and changes nothing. | by a developer, once per clone |
| `scan-secrets.sh` | gitleaks wrapper. `--staged` for the hook, `--all` for the working tree. Fails closed when gitleaks is absent. | pre-commit hook, CI, manually |
| `hooks/pre-commit` | Runs `scan-secrets.sh --staged`. Installed by setup via `core.hooksPath`. | every `git commit` |
| `check-kernel-size.sh` | Fails when `ServiceDefaults` exceeds its line ceiling (P2). | CI |
| `assert-authservice.sh` | Post-deploy checks on authservice: `/health`, `/health/ready`, discovery issuer and `RS256` only, a non-empty JWKS, and a 401 on `/api/v1/auth/me`. Fails on an empty key set. | `flyio.yml`, after the authservice deploy |

Numbering convention: when a runbook directory appears (deploy, scale, teardown),
its entries are numbered in the order they run (`0-setup-first-time`, `1-build`,
...) and delegate to descriptively named scripts. Phase A has only onboarding and
the scanner, so nothing is numbered yet.

## Where a secret lives, and the journey it takes

1. **Local store**: `dotnet user-secrets` for the AppHost project. Written by setup
   step 4 (`ANTHROPIC_API_KEY`, optional). Never a committed file. Phase E values
   are set by hand: `Reviews:SubjectPepper` (optional, keeps author hashes stable),
   `Authservice:JwtPrivateKeyPem` (only with `IncludeAuthservice=true`, see
   `docs/runbooks/AUTHSERVICE.md`). Phase L values (local seeding, ADR-0019): `SeedDemo`, `IncludeAuthservice`, `Seed:AdminEmail`, `Seed:TestPassword`
   and `Authservice:JwtPrivateKeyPem` are AppHost configuration, and all four are optional. When they are unset, the
   AppHost takes the generated values from `.local/seed-secrets.json` (gitignored, created on the first run with
   `SeedDemo` on). `DevSeed__Enabled` and `DevSeed__Key` are set by the AppHost and never by hand; the key is kept in
   the same file. See `docs/runbooks/LOCAL-DEV.md` section 6.
   Phase F values, also optional: `Billing:SubjectPepper`
   (keeps subject hashes stable) and `Billing:WebhookSecret` (only once a provider is chosen,
   see `docs/runbooks/BILLING.md`).
2. **AppHost parameter**: `builder.AddParameter(..., secret: true)`, read from user
   secrets as `Parameters:<name>`.
3. **Environment variable**: the AppHost passes it to the process as the variable
   name in the table below.
4. **Config key**: the service reads it through configuration, where `__` is the
   separator (`ConnectionStrings__catalogdb`, `SchoolSearch__...`).

The `.env` file is a convenience for scripts only. Services do not read it. Values in
`.env` are copied into user-secrets by setup, then `.env` is never consulted again by
the running system.

The parameter name `Parameters:anthropic-api-key` is the name setup writes. They must match the AppHost's `AddParameter` calls; if the
AppHost uses different names, change them in `setup.sh` and `setup.ps1` together.

## Variables by tier

Source of truth for names and tiers: [`../secrets.env.example`](../secrets.env.example).

| Variable | Tier | Where the value comes from | Degrades without it |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | always | set to `Development` locally | Production behaviour; no dev-only output |
| `DATABASE_PROVIDER` | mode | `postgres` or `sqlserver`; empty selects InMemory | InMemory: no data survives an API restart |
| `CATALOG_API_URL` | mode | the API's public URL; empty uses the candidate ladder | web falls back to `services__api__http__0`, then `http://localhost:8080` |
| `ConnectionStrings__catalogdb` | secret | the Postgres instance's connection string; never committed | with `DATABASE_PROVIDER=postgres` the API falls back to InMemory and `/health` reports it |
| `MCP_API_KEY` | secret | generated for the MCP service (Phase D) | MCP serves the anonymous rate-limit bucket only |
| `ConnectionStrings__reviewsdb` | secret | the Postgres instance, database `reviewsdb` | the Reviews service selects InMemory in Development and Staging; Production answers 503 |
| `Reviews__SubjectPepper` | secret | `openssl rand -hex 32`, never changed once reviews exist | with `SeedDemo`, the AppHost keeps a local pepper in `.local/seed-secrets.json`; without it, a random pepper per run (local only) |
| `SESSION_SECRET` | secret | `openssl rand -base64 48` | the BFF cannot seal its session cookie: account routes answer 503 |
| `BFF_SHARED_SECRET` | secret | `openssl rand -base64 32`, same value for web, api and reviews | forwarded client addresses are not trusted; the anonymous bucket applies |
| `Auth__Authority`, `Auth__Issuer`, `Auth__Audience` | mode | authservice public origin and the issuer and audience it issues | tokens are not validated; auth-gated endpoints answer 503 `auth_unavailable` |
| `AUTH_BASE_URL` | mode | authservice origin for the web BFF | account pages show the unavailable state |
| `REVIEWS_BASE_URL` | mode | Reviews service origin for the web BFF | the review section shows the unavailable state |
| `Dsa__ContactEmail` | mode | the owner's single point of contact (DSA art. 11/12); never invented | `GET /api/v1/legal/contact` answers 503 `contact_not_configured` |
| `AI_ENABLED` | mode | `true` to enable review triage (E6) | triage idle; moderator queue shows no AI label |
| `AUTHSERVICE_JWT_PRIVATE_KEY_PEM` | ci | the Fly secret source; generated once, see the runbook | the authservice deploy job fails before any change |
| `Billing__MockMode` | mode | `true` locally (default outside Production); `false` in Production | synthetic checkout and simulate on outside Production; Production answers checkout 503 `billing_unavailable` |
| `Billing__CheckoutBaseUrl` | mode | the payment provider's checkout origin, set only with a provider adapter (ADR-0016) | mock checkout only; Production answers 503 `billing_unavailable` |
| `ConnectionStrings__billingdb` | secret | the Postgres instance, database `billingdb` | the billing service selects InMemory outside Production; Production answers 503 |
| `Billing__SubjectPepper` | secret | `openssl rand -hex 32`, never changed once claims or subscriptions exist | with `SeedDemo`, the AppHost keeps a local pepper in `.local/seed-secrets.json`; without it, a per-process pepper outside Production; Production refuses writes |
| `Billing__WebhookSecret` | secret | the payment provider's dashboard, once a provider is chosen | mock mode accepts unsigned webhooks outside Production; Production rejects every webhook |
| `BILLING_BASE_URL` | mode | the billing service origin for the web BFF | billing features show the unavailable state; catalog and reviews are unaffected |
| `BILLING_SUBJECT_PEPPER` | ci | GitHub environment `dev` secret; becomes the Fly secret `Billing__SubjectPepper` | the billing deploy job fails before any change |
| `BILLING_WEBHOOK_SECRET` | ci | GitHub environment `dev` secret; becomes the Fly secret `Billing__WebhookSecret` | the billing deploy continues; every webhook is rejected in Production until it is set |
| `SeedDemo` | mode | AppHost configuration (user-secrets or environment), `true` by default | with `false` the catalog starts empty, no seeder, `/dev/seed` answers 404 (`docs/runbooks/LOCAL-DEV.md`) |
| `IncludeAuthservice` | mode | AppHost configuration; follows `SeedDemo` when unset | no authservice container: no accounts, sign-in shows the unavailable state |
| `Seed__AdminEmail` | mode | AppHost configuration; default `admin@school-search.test` | the SuperAdmin account keeps the default email |
| `Seed__TestPassword` | secret | AppHost user-secrets, `dotnet user-secrets set "Seed:TestPassword" ...`; otherwise the value in `.local/seed-secrets.json` | the AppHost generates the password once and keeps it in the local store; the seeder prints it in its log on each run unless it is set in user secrets |
| `Authservice__JwtPrivateKeyPem` | secret | AppHost user-secrets (local dev key, never the Fly key); otherwise the value in `.local/seed-secrets.json` | the AppHost generates the key once and keeps it in the local store; tokens survive restarts. Without `SeedDemo` and with `IncludeAuthservice=true` the AppHost stops until it is set |
| `DevSeed__Enabled` | mode | set by the AppHost when `SeedDemo` is on, never by hand | the `/dev/seed` endpoints of Reviews and Billing answer 404 |
| `DevSeed__Key` | secret | set by the AppHost, never by hand: generated once and kept in `.local/seed-secrets.json` | the `/dev/seed` endpoints answer 404 without the matching `X-Dev-Seed-Key` |
| `AUTHSERVICE_IMAGE` | ci | repository variable: the pinned tag, for example `ghcr.io/konradcinkusz/authservice:v0.3.4` | the authservice deploy job fails before any change |
| `ANTHROPIC_API_KEY` | secret, optional | setup step 5, or your own key | AI features disabled; `/health` lists `ai` as disabled |
| `FLY_API_TOKEN` | ci | `fly tokens create org`, stored in the GitHub `production` environment | not read locally; the first `flyctl` call in `flyio.yml` fails, which is the intended failure point |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | tuning | an OTLP collector URL | no export; telemetry stays in the process console |

Zero credentials: with every secret empty, the system starts and `GET /health`
answers 200 with each optional integration listed as degraded or disabled. That is
the test of the scaffold.

## Recipes

- **Reset local secrets**: `dotnet user-secrets clear --project src/SchoolSearch.AppHost/SchoolSearch.AppHost.csproj`, then `scripts/setup.sh` (or `.ps1`).
- **Reset the local demo data and its generated values**: stop the AppHost, then delete `.local/seed-secrets.json` and the Docker volume `school-search-postgres-data` together. Deleting only the file breaks sign-in. See `docs/runbooks/LOCAL-DEV.md` section 7.
- **Check what is installed**: `scripts/setup.sh --check` (or `scripts/setup.ps1 -Check`).
- **Scan before committing without the hook**: `scripts/scan-secrets.sh --staged`.
- **Scan the whole working tree**: `scripts/scan-secrets.sh --all`.
- **Skip the hook for one commit**: not supported. A commit that trips the scanner is
  a finding; rotate the value first, then fix the file. (Architecture-standards
  REPO-BASELINE §2: rotate before history-scrubbing.)

## Troubleshooting

Keyed on the literal text you will see.

| Output | Cause | Fix |
|---|---|---|
| `scan-secrets: gitleaks is not installed` | gitleaks absent; the commit is refused on purpose | install gitleaks 8.19+ (link in the message), then retry |
| `Required tools missing: gitleaks 8.19+` | same, reported by `--check` | as above |
| `.NET 10 SDK (wrong version)` | `dotnet` on PATH is not 10.x | install the 10.0 SDK; `global.json` pins the band |
| `A compatible .NET SDK was not found` | the SDK named in `global.json` is not installed | install that SDK band, or `dotnet --list-sdks` to see what is present |
| `.../SchoolSearch.AppHost.csproj` not found, or `step 4 ... not present yet` | the solution is not on this branch yet | pull the branch that adds it, then re-run step 4 |
| `./scripts/hooks/pre-commit: Permission denied` | the hook lost its execute bit | `chmod +x scripts/hooks/pre-commit scripts/scan-secrets.sh`, or re-run setup |
| `fatal: not a git repository` | setup was run outside a clone | clone the repository, then run setup from it |
| `gitleaks reported findings` | a secret-shaped value is in the tree or staged | rotate the value, remove it, re-run `--all` |
| `Permission denied` on `scripts/setup.sh` | execute bit not set on a fresh download | `bash scripts/setup.sh` |
| `scripts/setup.ps1 cannot be loaded because running scripts is disabled` | Windows execution policy | `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`, then re-run |

## Conventions for new scripts

- `set -euo pipefail` in bash; `Set-StrictMode` and `$ErrorActionPreference = 'Stop'` in PowerShell.
- A script that needs a secret reads it from the environment or user-secrets and
  refuses to run if it is missing, naming the variable.
- Scripts that mirror a CI job read secrets from `.env`, never inline.
- A new variable is added to `secrets.env.example` with its tier and a `degrades:` line in the same commit.
