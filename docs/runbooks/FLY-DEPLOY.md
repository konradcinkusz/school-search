# Runbook: deploying school-search to Fly.io (for the owner)

Audience: the repository owner, doing the first deploy and every one after. Language: English. Deploys come from a `v*` tag in
CI (`.github/workflows/flyio.yml`). **Agents never deploy, never run `flyctl`, never create Fly apps.** Every command below is
for you to run, or is run by CI on your tag.

Related: `flyio/SECRETS.md` (every secret, in full), `flyio/INFRASTRUCTURE-ANALYSIS.md` (sizing and cost estimates),
`docs/adr/0020-fly-topology-and-consolidation.md` (why this many apps), `docs/runbooks/AUTHSERVICE.md`,
`docs/runbooks/BILLING.md`, `docs/runbooks/AI-OPERATIONS.md`.

## 0. What has and has not been run

Everything in this runbook and in `flyio/` and `.github/workflows/flyio*.yml` was **written and read, not executed**.

| Item | State |
|---|---|
| `actionlint` (v1.7.7, with shellcheck) over all workflows | RUN, passed, on the author's machine. CI runs the same check |
| `flyio/check-config.sh` over the seven fly.toml files | RUN, passed |
| `flyio/*.sh` through shellcheck | RUN, passed |
| The change-detection step of `flyio.yml`, against a throwaway git repository with a stubbed `flyctl` | RUN: Seeder, AppHost, tests and docs-only changes select nothing; web-only selects web; kernel selects all .NET services |
| Any `flyctl` command, any deploy, any Fly app, volume or secret | **NOT RUN** |
| `ensure_databases` (psql over `flyctl ssh console`), `flyctl volumes snapshots create`, `registry.fly.io` mirroring | **NOT RUN, flag and syntax unverified against your flyctl version** |
| The .NET build and tests, the web build | **NOT RUN here** (no SDK). CI runs them on the tag (`test` job) |
| Any authservice behaviour (G1 to G6 in `docs/runbooks/AUTHSERVICE.md` section 7) | **NOT RUN, UNVERIFIED** |
| Any cold-start or cost number | **ESTIMATE** (INFRASTRUCTURE-ANALYSIS), not measured |

Expect the first tag to need one or two fix-ups. The pipeline fails closed (a missing secret stops the job before anything
changes), so a failed first run does not leave a half-configured service serving traffic.

## 1. The estate in one table

| Fly app | Kind | Idle state | Reached by |
|---|---|---|---|
| `school-search-postgres` | Postgres 17 on a 10 GB volume, no public listener | running | `school-search-postgres.internal:5432` |
| `school-search-authservice-dev` | external identity provider image (pinned) | running (1) | public URL (JWKS) |
| `school-search-api-dev` | catalog API | running (1) | public URL |
| `school-search-reviews-dev` | reviews, DSA, moderation | running (1) | public URL |
| `school-search-billing-dev` | claims, enquiries, subscriptions | running (1) | public URL |
| `school-search-mcp-dev` | MCP server and advisor agent | stopped (0) | public URL |
| `school-search-web-dev` | Next.js BFF and site | stopped (0) | browsers |

Region `waw` for everything (ADR-0002). Deploy order: **postgres, databases, authservice, api, reviews, billing, mcp, web.**

## 2. One-time setup (about 30 minutes, before the first tag)

### 2.1 Fly organisation and token

1. Create or choose a Fly organisation. The workflow uses `FLY_ORG: personal`; change it in `flyio.yml` if you use another.
2. Create an **organisation** token (it must be able to create apps and volumes, which an app-scoped deploy token cannot):
   `fly tokens create org` (check `fly tokens create --help` for your flyctl version). Copy it once.
3. Add it as the **environment** secret `FLY_API_TOKEN` (next section). Do not put it in a repository secret, a file or a chat.
4. Add a payment method to the Fly account if Fly requires one for volumes and always-on machines.

### 2.2 GitHub environment `dev`

Settings, Environments, New environment: `dev`.

- Strongly recommended: **required reviewers** (yourself), so every deploy job waits for an approval click, and **deployment
  branches and tags** limited to `v*` tags and `main`.
- Add the secrets and variables below. Names only here; **no value is ever written in the repository**.

**Environment secrets** (Settings, Environments, dev, Environment secrets):

| Name | Required | Where the value comes from |
|---|---|---|
| `FLY_API_TOKEN` | yes | `fly tokens create org` (2.1) |
| `POSTGRES_PASSWORD` | yes | generate: `openssl rand -base64 32`. Read only when the empty volume is first initialised; after that it cannot be changed with the secret alone (needs `ALTER USER`) |
| `SESSION_SECRET` | yes | `openssl rand -base64 48` (the job refuses fewer than 32 characters) |
| `BFF_SHARED_SECRET` | yes | `openssl rand -base64 32`. **One value**: CI pushes the same secret to web, api, reviews, billing (and mcp), so they cannot diverge |
| `REVIEWS_SUBJECT_PEPPER` | yes | `openssl rand -hex 32`. **Never change it after the first review** |
| `BILLING_SUBJECT_PEPPER` | yes | `openssl rand -hex 32`, a **different** value from the reviews pepper (the job refuses equal values). Never change it after the first claim |
| `AUTHSERVICE_JWT_PRIVATE_KEY_PEM` | yes, if authservice is enabled | `docs/runbooks/AUTHSERVICE.md` section 1: RSA 2048 PKCS#8, stored on one line with literal `\n` |
| `GOOGLE_MAPS_API_KEY` | optional | Google Cloud console, a **browser** key for the map page. Restrict it by **HTTP referrer** to your site origin(s) and to the Maps JavaScript API (plus Geocoding API if the page uses it). Without it `/mapa` shows its list fallback |
| `ANTHROPIC_API_KEY` | optional | Anthropic console, a key for this project only. Pushing it enables nothing: every `AI_ENABLED` stays `false` (section 10 of this file and `docs/runbooks/AI-OPERATIONS.md`) |
| `MCP_API_KEY` | optional | a key issued by the Api's free-key mechanism, only if you want mcp out of the anonymous bucket |
| `BILLING_WEBHOOK_SECRET` | optional until a payment provider is chosen | the provider's dashboard (`docs/runbooks/BILLING.md` sections 1 and 4). Without it every webhook is rejected (correct) |
| `GHCR_PULL_TOKEN` | optional | only with `FLY_REGISTRY_MIRROR=true` and a private authservice package: a token with `read:packages` |

**Variables** (Settings, Environments, dev, Environment variables, or repository variables):

| Name | Value | Meaning |
|---|---|---|
| `AUTHSERVICE_ENABLED` | `true` | deploy authservice. Leave unset or `false` to run without accounts (P8) |
| `AUTHSERVICE_IMAGE` | `ghcr.io/konradcinkusz/authservice:<newest release tag>` | a tag, never `:latest`. Pin the newest release; the doc was written against v0.3.4 |
| `MCP_ENABLED` | `false` to leave mcp undeployed until agent traffic exists | unset means deploy it |
| `FLY_REGISTRY_MIRROR` | `true` only if the GHCR packages stay private (2.4) | |
| `GHCR_USER` | your GitHub user | only with the mirror and `GHCR_PULL_TOKEN` |
| `SKIP_DB_BOOTSTRAP` | `true` only if you create the databases by hand (section 3) | |

**Never set, anywhere in Fly or GitHub for this estate:** `DevSeed__Enabled`, `DevSeed__Key`, `SCHOOLSEARCH_DEMO_SEED`, `Billing__MockMode=true`,
`AI_ENABLED=true`. `flyio/check-config.sh` fails the `test` job if a fly.toml sets any of the first three or leaves `MockMode` not `false`.

### 2.3 Regions, volumes, apps

Nothing to create by hand. The first tag creates every app (`flyctl apps create`) and the one volume
(`school_search_pgdata`, 10 GB, region `waw`) idempotently. A volume lives in one region on one host: it cannot be moved by
editing a file. If you change the region, change `FLY_REGION` in `flyio.yml` **and** every `primary_region` before the first tag.

### 2.4 GHCR visibility (read this: the first deploy fails here if you skip it)

CI pushes images to `ghcr.io/konradcinkusz/school-search-<service>` (ADR-0003) and deploys with `flyctl deploy --image`. **Fly
pulls that image itself and receives no GHCR credential from this repository.** The earlier idea of a `GHCR_USER` / `GHCR_TOKEN`
Fly secret is not implemented and is not verified as a way for Fly to pull private images. Choose one:

- **(a) Make the packages public** (simplest). After the first build the packages exist and are private by default. GitHub, your
  profile or org, Packages, each of `school-search-api`, `-reviews`, `-billing`, `-mcp`, `-web`, Package settings, Change
  visibility, Public. The images hold no secret (`.dockerignore` excludes env files, keys, docs and tests) but they do contain
  your compiled application and the embedded methodology document: that is your decision. The packages cannot exist before the
  first build, so on a first tag either let the first run fail at the image pull and re-run after flipping them, or use (b).
- **(b) Mirror to Fly's registry.** Set `FLY_REGISTRY_MIRROR=true`. Each deploy job then pulls from GHCR on the runner, pushes to
  `registry.fly.io/<app>:<tag>` and deploys that. Nothing is public. Not run before: verify on the first tag.

**authservice image** (`ghcr.io/konradcinkusz/authservice`) belongs to another repository. It must be public there, or you set
`FLY_REGISTRY_MIRROR=true` plus `GHCR_PULL_TOKEN` (`read:packages`). `flyio/ghcr-probe.sh` prints a warning in each deploy job when
an image is not anonymously pullable and the mirror is off.

### 2.5 Things to decide before real users

- Postgres data safety (section 9). Do not let real reviews or billing records accumulate before you have decided.
- `Dsa__ContactEmail` in `flyio/reviews.fly.toml` (owner-supplied, deliberately unset): without it the DSA contact endpoint answers 503.
- Payment provider (`docs/runbooks/BILLING.md` section 5). Until then billing runs with `MockMode=false` and no checkout.
- The AI open items (`docs/runbooks/AI-OPERATIONS.md`).

## 3. Databases

The `ensure_databases` job creates `reviewsdb`, `billingdb` and `authservicedb` (and checks `catalogdb`) inside the Postgres machine
with `psql` over `flyctl ssh console`, before any service that owns one deploys. It is idempotent. **It has not been run.** All
four databases are served by one Postgres role (`schoolsearch`, a superuser) with one password: see ADR-0020.

If the job fails, create them yourself and tell the pipeline to skip the job:

```bash
flyctl proxy 15432:5432 --app school-search-postgres &        # a local tunnel over 6PN; Postgres has no public listener
read -r -s -p "POSTGRES_PASSWORD: " PGPASSWORD; echo; export PGPASSWORD
psql "host=localhost port=15432 user=schoolsearch dbname=catalogdb" \
  -c "CREATE DATABASE reviewsdb;" -c "CREATE DATABASE billingdb;" -c "CREATE DATABASE authservicedb;"
unset PGPASSWORD; kill %1
```

then set the repository variable `SKIP_DB_BOOTSTRAP=true`. "already exists" on a re-run is the expected answer. Schemas are applied
by each service itself (EF migrations from a hosted service, after the listener is up), never by the pipeline.

## 4. Pre-flight checklist (tick before `git tag`)

- [ ] `main` is green in CI (`ci-required`, including `actionlint`, the Docker smoke tests and Playwright).
- [ ] Environment `dev` exists with reviewers, and holds every **required** secret in 2.2 (names checked, not values).
- [ ] `AUTHSERVICE_ENABLED`, `AUTHSERVICE_IMAGE` set (or you accept no accounts); the authservice image pull path is decided (2.4).
- [ ] The GHCR path is decided: packages to be flipped public after the first build, or `FLY_REGISTRY_MIRROR=true`.
- [ ] `bash flyio/check-config.sh` passes locally. `AI_ENABLED` is `"false"` in api, reviews, billing, mcp.
- [ ] `Billing__MockMode = "false"` and no `DevSeed__*` / `SCHOOLSEARCH_DEMO_SEED` in any fly.toml (the script checks both).
- [ ] You have read section 9 and accepted, or fixed, the Postgres risks.
- [ ] A Google Maps key (if used) is restricted by HTTP referrer to the final site origin **before** you store it.
- [ ] You know you will push **one tag at a time** and wait for it (section 5).
- [ ] You have a way to read logs: `flyctl logs --app <app>`.

## 5. The first release

```bash
git switch main && git pull
git tag v0.1.0
git push origin v0.1.0
```

(Version numbers are yours to choose; the workflow reacts to any tag starting with `v`.) Then watch the `flyio` run in the Actions tab.
**Push one tag at a time and wait for it to finish.** The workflow's concurrency group keeps one run queued behind the running one,
but GitHub replaces an already-waiting run with a newer one, and change detection compares each tag with the previous tag: a
skipped tag's changes would otherwise never be deployed. If you ever suspect that, run the workflow by hand with service `all`
(section 7).

What each job does (all in `.github/workflows/flyio.yml`):

| Job | What it does |
|---|---|
| `resolve ref` | works out the commit and the image tag (the tag name, or `manual-<service>-<sha7>` for a manual run) |
| `test` | `flyio/check-config.sh`, `dotnet restore/build -warnaserror/test`, kernel size check. Nothing deploys if it fails |
| `detect changes` | diffs against the previous `v*` tag. A service whose Fly app is missing is **always** selected, so the first tag selects everything (including `school-search-import`, which is built and pushed but never deployed). Seeder, AppHost, tests and docs (except `docs/methodology`) select nothing |
| `build <service>` | one image per selected service, built once, pushed to GHCR as `:<tag>` and `:sha-<commit>` |
| `deploy postgres` | creates the app, the volume and the `POSTGRES_PASSWORD` secret if missing, takes a best-effort volume snapshot, deploys with `--ha=false`. Runs only when its fly.toml changed or the app is missing |
| `ensure databases` | creates the logical databases (section 3) |
| `deploy authservice` | pinned image, `Jwt__PrivateKeyPem` and its connection string; then `scripts/assert-authservice.sh` (health, readiness, issuer, RS256, non-empty JWKS) |
| `deploy api`, `reviews`, `billing`, `mcp`, `web` | in that order. Each: create app if missing, check its required secrets (fail before changing anything), stage secrets on stdin, deploy the built image, run `flyio/smoke.sh <service>` |

A job runs only if all of its upstream jobs succeeded or were skipped. A failure anywhere stops everything downstream.

Secrets are staged (`--stage`) and applied by the deploy that follows, so one release restarts a service once.

### 5.1 Smoke tests (automatic, and what to check by eye)

Automatic, after each deploy (`flyio/smoke.sh`): `/health` and `/alive` on every .NET service (a *degraded* integration is reported in
the body and is not a failure); api `/api/v1/meta` returns JSON; reviews and billing answer **404** on `POST /dev/seed` (seed is off
in Production); billing refuses `POST /api/v1/billing/simulate` (401/403, never 2xx) and an unsigned webhook; web `/healthz`, `/`
HTML, `/api/config` reads the runtime `CATALOG_API_URL`, and the BFF proxy reaches the api (`/api/proxy/api/v1/meta`).

By eye, in a browser, after the first tag:

- [ ] `https://school-search-web-dev.fly.dev/` loads (first request after idle is slow: scale to zero).
- [ ] Search returns results **only if data was imported** (the catalog is empty until the import job runs: `docs/runbooks/IMPORT.md`).
- [ ] `/mapa` shows the list fallback, or the map when `GOOGLE_MAPS_API_KEY` was set. Check the browser console for CSP violations.
- [ ] Register and log in (needs authservice). If the discovery URL redirects forever, see G3 in `flyio/authservice.fly.toml`.
- [ ] `flyctl status --app <app>` for each app; `flyctl secrets list --app <app>` shows names and digests only.
- [ ] `/doradca` says the advisor is unavailable (correct while `AI_ENABLED=false`).

## 6. Per-service behaviour worth knowing

- **BFF secret.** web, api, reviews and billing must hold the same `BFF_SHARED_SECRET`; mismatch means every visitor falls into one
  shared rate-limit bucket. One GitHub secret feeds all of them.
- **Auth.** `Auth__Authority` / `Auth__Issuer` / `Auth__Audience` in api, reviews, billing must equal authservice's `Jwt__PublicBaseUrl` /
  `Jwt__Issuer` / `Jwt__Audience`; `AUTH_BASE_URL` (web) must be the same public URL. The post-deploy assertion reads the issuer from
  `flyio/authservice.fly.toml`.
- **Map key.** `GOOGLE_MAPS_API_KEY` is a runtime Fly secret on web only. Rotating it is: change the GitHub secret, re-run the web deploy.
- **Migrations.** Each service migrates its own database after its listener is up. Keep each service at **one machine** until a migration
  lock exists (ADR-0020, `flyio/INFRASTRUCTURE-ANALYSIS.md` section 7); `flyio-scale.yml` `request-path-2` needs an explicit acknowledgement for that reason.

## 7. Hotfix or re-run one service

Actions, `flyio`, Run workflow:

- `service`: `api`, `reviews`, `billing`, `mcp`, `web`, `authservice`, or `all` (re-run every service at the ref; Postgres only if its app is missing).
- `ref`: a `v*` tag, or a commit that is on `main`. Empty means the commit the run was started from (which must be on `main`).

It runs `test`, builds that one image as `manual-<service>-<sha7>`, deploys it with the same secret checks and smoke test, and leaves
the other apps alone. Postgres is never redeployed by hand. A hotfix on an unmerged branch is refused on purpose: merge or tag it first.

## 8. Rollback (commands for you to run)

An image rollback does not undo a database migration, and it does not undo a secret change. Read the release's migration list first;
a migration that dropped or renamed something is not safely reversible by redeploying an older image.

```bash
flyctl releases --app school-search-api-dev                 # list releases; note the image of the last good one
flyctl status   --app school-search-api-dev                 # what runs now
# Redeploy the previous image tag (the images are kept in GHCR as :vX.Y.Z and :sha-<commit>):
flyctl deploy --config flyio/api.fly.toml --app school-search-api-dev \
  --image ghcr.io/konradcinkusz/school-search-api:v0.1.0 --yes
```

Use the config file from the **same tag** you are rolling back to (`git switch --detach v0.1.0` first), so `[env]` matches the image.
If you mirrored to `registry.fly.io`, the previous image is `registry.fly.io/<app>:<tag>`. For billing see `docs/runbooks/BILLING.md` section 7.
To roll back through the pipeline instead, run the workflow by hand (section 7) with `ref` set to the old tag: it rebuilds from source.
To stop an AI feature fast: `flyctl secrets unset ANTHROPIC_API_KEY --app <app>` (restarts that app) or a reviewed commit and a tag.

## 9. Postgres data safety (read before real users)

State of the repository (details and wording in ADR-0020 and INFRASTRUCTURE-ANALYSIS section 9):

- One vanilla `postgres:17-alpine` machine and one volume in one region. No replica, no managed failover.
- Fly keeps platform snapshots of volumes on its own schedule; **this repository neither configures nor checks it**. The pipeline takes one more, best effort, before each Postgres redeploy.
- **Missing:** a logical backup (`pg_dump`) stored outside Fly, point-in-time recovery, a tested restore, alerting on disk usage.
- All services share one database role (a superuser). The boundary between databases is by connection string and code, not by permission.

Commands to inspect (for you to run):

```bash
flyctl volumes list --app school-search-postgres
flyctl volumes snapshots list <volume-id>
flyctl volumes snapshots create <volume-id>           # a manual restore point
flyctl proxy 15432:5432 --app school-search-postgres  # then: pg_dump -h localhost -p 15432 -U schoolsearch -Fc reviewsdb > reviewsdb.dump
```

Do a restore drill into a scratch Fly app **before** you rely on any of this. Restoring from a volume snapshot creates a new volume; check
the current Fly documentation for the exact `flyctl volumes create --snapshot-id` flow, which is not scripted here.

## 10. AI stays off

`ANTHROPIC_API_KEY` is optional. CI pushes it to api, reviews, billing and mcp only when the GitHub secret exists. Every service's
`AI_ENABLED` is `"false"` in its fly.toml (the Api, Reviews and Billing default to ON in code, which is why the lines are stated
and checked). To switch a feature on, follow `docs/runbooks/AI-OPERATIONS.md` section 3: eval gate, open items, a reviewed one-line change, a tag.

## 11. Cost levers and the demo environment

Cost levers (full reasoning and the figures, all estimates, in ADR-0020 and `flyio/INFRASTRUCTURE-ANALYSIS.md`):

- Leave `mcp` undeployed until agent traffic exists: `MCP_ENABLED=false`. Web and mcp already scale to zero.
- Keep every machine `shared-cpu-1x`; keep Postgres at one machine; do not enable HA at launch.
- Lower `min_machines_running` of `reviews` or `billing` to 0 if you accept a cold start on public pages (the tomls explain the call that pins them).
- `flyio-scale.yml` profile `stop` destroys the stateless machines when the estate is idle; `start` re-creates one per app. Postgres is never touched.
- Merging services into "one API app" is **not** a lever: it is a design change (ADR-0020).

**Demo environment (an option, not built).** A demo with seeded data must be a separate estate, never the production apps:
separate app names (for example `school-search-*-demo`), its own Postgres app and volume (so demo data can never share a database
with real data), `ASPNETCORE_ENVIRONMENT = "Staging"` on the Api so `SCHOOLSEARCH_DEMO_SEED=true` is honoured (the demo seed refuses
Production), no real authservice signing key, no payment secrets, and its own GitHub environment (`demo`) with its own secrets.
`SCHOOLSEARCH_DEMO_SEED` is allowed **only** there. The repository supports this by convention only: put the demo tomls in
`flyio/demo/` (outside the `flyio/*.fly.toml` glob that `check-config.sh` guards) and add a manual-only workflow that deploys them.
Neither exists yet; building it is a separate task, and the Production guard keeps the seed off the production apps regardless.

## 12. Teardown

`flyio-destroy.yml` (typed confirmation `DESTROY school-search dev`) destroys the stateless apps in reverse order and keeps Postgres and
its volume unless you tick `delete_database` (irreversible). Taking a snapshot and a `pg_dump` first is advisable (section 9).
