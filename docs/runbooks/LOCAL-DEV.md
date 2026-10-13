# Runbook: local development with seeded demo data

This runbook starts the whole product on your machine with one command and signs you in as a test account
in each role. It is for local development only. It is not the production topology (that is `flyio/` and the
tag-driven workflow, see `docs/runbooks/AUTHSERVICE.md`).

Every record that this setup creates is synthetic and labelled DEMO. No real school, pupil, parent, address
or credential is involved. See [Safety](#safety).

> **Status of this document.** It describes the contract that the implementation follows (ADR-0019). It was
> written without a .NET SDK or Docker on the machine that wrote it. The first real run has not happened yet.
> See [What is not verified](#what-is-not-verified).

## 1. Prerequisites

| Tool | Why | Check |
|---|---|---|
| .NET 10 SDK | builds and runs the AppHost and every service (`global.json` pins the band) | `dotnet --version` starts with `10.` |
| Docker (Docker Desktop, Colima or Podman with the Docker API) | Aspire starts Postgres, and authservice when enabled, as containers | `docker info` answers |
| Node.js and pnpm | the web app in `web/app` | `node --version`, `pnpm --version` |
| `openssl` (optional) | only if you want a stable local signing key | `openssl version` |

Optional: run `./scripts/setup.sh` (or `scripts\setup.ps1` on Windows) once per clone. It checks the tools and
writes `dotnet user-secrets` for the optional values. `./scripts/setup.sh --check` lists what is missing and
changes nothing. Setup is not required for the seeded run.

## 2. Start everything

```bash
dotnet run --project src/SchoolSearch.AppHost
```

That is the one command. With the defaults (`SeedDemo` is `true`), it:

1. starts a Postgres container with the databases `catalogdb`, `reviewsdb`, `billingdb` and `authservicedb`;
2. starts the catalog API, the Reviews service, the Billing service and the MCP service;
3. starts authservice as a container on `http://localhost:8090` (because `SeedDemo` turns it on);
4. generates an ephemeral RS256 signing key for authservice, unless you set `Authservice:JwtPrivateKeyPem`;
5. starts the one-shot `seeder` after authservice, Reviews and Billing are healthy. It creates the test
   accounts and loads the demo data (section 5), then exits;
6. starts the web app.

The console prints the address of the Aspire dashboard when it starts. Open it in a browser.

## 3. The Aspire dashboard

The dashboard lists these resources. Each row has its endpoints, health and logs.

| Resource | What it is | Where to look |
|---|---|---|
| `postgres` | Postgres container with `catalogdb`, `reviewsdb`, `billingdb`, `authservicedb`. Data in the Docker volume `school-search-postgres-data` | the resource's logs |
| `api` | catalog API (`/api/v1/...`, `/health`) | endpoint `http` |
| `reviews` | Reviews service: reviews, DSA notices, moderation | endpoint `http`, `/health` |
| `billing` | Billing service: sponsored profiles, claims, enquiries (mock checkout) | endpoint `http`, `/health` |
| `mcp` | MCP service and the agent endpoint (`/mcp`, `/agent/chat`) | endpoint `http`, `/health` |
| `authservice` | identity provider container (only when `SeedDemo` or `IncludeAuthservice` is on) | `http://localhost:8090`, `/health/ready` |
| `seeder` | one-shot job that creates accounts and calls the dev seed endpoints. It shows as finished | its **console logs**: the summary and the generated password (section 6) |
| `web` | the Next.js product surface | endpoint `http` (the port is assigned by Aspire; the dashboard shows it) |

Ports are assigned by Aspire, except authservice (fixed at `8090`). Use the dashboard to find the web address.
Open the web app at its `http` endpoint, for example `/` for the catalog and `/konto/logowanie` to sign in.

## 4. What is seeded

Seeding runs in three places: the catalog API (on its own start), the Reviews and Billing services (through
`POST /dev/seed`), and the seeder (accounts). All seeded data is idempotent: running the seeder again changes
nothing.

**Catalog** (`src/SchoolSearch.Api/Demo`, flag `SCHOOLSEARCH_DEMO_SEED=true`, which the AppHost sets from `SeedDemo`):

- the gmina **Przykładowo** (TERYT `9999999`), with a street rule set for the street *Przykladowa*;
- schools `demo-sp-1` to `demo-sp-14` (for example `demo-sp-1` is published, `demo-sp-3` has too few pupils to
  publish a score, `demo-sp-6` has no exam data);
- five demonstration preschools (`przedszkole-demonstracyjne-nr-1` and onwards).

Try `/?gmina=9999999&ulica=Przykladowa&numer=5` for a catchment result, `/szkoly/demo-sp-1` for a school page,
and `/ranking/gmina:9999999` for a ranking.

**Reviews, DSA and moderation** (`POST /dev/seed` on Reviews):

- published reviews on `demo-sp-1`, `demo-sp-2` and `demo-sp-3`, from two parents, with different ratings and themes;
- a review waiting for moderation;
- a review hidden by moderation, with its statement of reasons (DSA);
- an owner reply;
- DSA notices in each state: open, decided and appealed. One of them carries an acknowledgement token;
- a complaint, and review-triage rows when the triage table exists (labels only, no AI call).

Reviews never change a school's score.

**Billing and owner data** (`POST /dev/seed` on Billing, mock mode):

- an approved claim by the owner on `demo-sp-1`, with an active mock subscription and a published sponsored
  profile text, marked as sponsored and DEMO;
- a pending claim by the owner on `demo-sp-2`, for the moderator's queue;
- several anonymous, synthetic parent enquiries in the owner's inbox, one of them already answered.

Billing never changes a school's score.

## 5. Test accounts

The seeder creates these accounts through authservice's public API (`register`, then `login` to read each `sub`).
It assigns the roles through authservice's admin API.

| Email | Role in authservice | What it is for | What to open |
|---|---|---|---|
| `admin@school-search.test` (`Seed:AdminEmail`) | `SuperAdmin` (created by authservice's initial-admin setting) | the administrator of the whole product | everything the moderator can open, plus the authservice admin API (roles, lock, unlock) |
| `moderator@school-search.test` | `Admin` | moderation: review queue, DSA notices and statements of reasons, school corrections, claims | `/moderacja` |
| `parent@school-search.test` | `User` | a parent: writes reviews, files a notice about content | a school page such as `/szkoly/demo-sp-1` (write a review there), `/zglos-tresc` (notice), the account pages under `/konto` |
| `parent2@school-search.test` | `User` | a second parent, for the second set of reviews and the reply flow | the same pages as `parent` |
| `owner@school-search.test` | `User` | a school owner, with an approved claim on `demo-sp-1` (active sponsored profile) and a pending claim on `demo-sp-2` | `/konto/szkola` (the owner's schools), `/konto/szkola/demo-sp-1` (sponsored profile, enquiries inbox) |

All accounts except `admin` use the same password, `Seed:TestPassword`. The `admin` account is created by
authservice with the same password, so one password signs you in everywhere.

The role appears in the access token. An account with the role `Admin` or `SuperAdmin` passes the API's admin
group (ADR-0015). A `User` does not.

### How to get the password

There are two ways. Pick one.

1. **Generated per run (the default).** The seeder generates a strong random password when `Seed:TestPassword` is
   not set. It prints the password **once**, in the summary, in the **console logs of the `seeder` resource** in
   the Aspire dashboard. Copy it then. It is not written to a file and not committed.
2. **Fixed, from your user secrets.** Set `Seed:TestPassword` once. The seeder reuses it and does not print it:

   ```bash
   dotnet user-secrets set "Seed:TestPassword" "<a password of your choice>" --project src/SchoolSearch.AppHost
   ```

   Use this when you want the same password on every run. Do not put the value in a file in the repository.

## 6. Flags and secrets

Set them with `dotnet user-secrets` on the AppHost project (the usual way), or as environment variables for one run.

| Setting | Default | What it does |
|---|---|---|
| `SeedDemo` | `true` | master switch: catalog demo seed, the seeder, authservice, and the dev seed endpoints. Set `false` to run without seeding (section 8) |
| `IncludeAuthservice` | follows `SeedDemo` | run authservice as a container. Set it `true` to have authservice without the demo seed |
| `Authservice:JwtPrivateKeyPem` | absent | RS256 private key for authservice, PKCS#8 PEM on one line with literal `\n`. Absent: an ephemeral key is generated per run, so tokens from a previous run stop working after a restart. Set it only when you want tokens to survive restarts. Generate one as in `docs/runbooks/AUTHSERVICE.md` section 1, never reuse the production key |
| `Seed:AdminEmail` | `admin@school-search.test` | the email of the SuperAdmin account |
| `Seed:TestPassword` | generated per run | the password of all test accounts (section 5) |
| `ANTHROPIC_API_KEY` | absent | passed to the API, Reviews, Billing and MCP when present. Without it the AI features answer `503 ai_unavailable` and the rest works |
| `AI_ENABLED` | absent (off) | the switch for the AI features. Set `true` together with the key |

Example for the AI features:

```bash
dotnet user-secrets set "ANTHROPIC_API_KEY" "<your key>" --project src/SchoolSearch.AppHost
dotnet user-secrets set "AI_ENABLED" "true" --project src/SchoolSearch.AppHost
```

The seeder's own variables (`DEV_SEED_KEY`, `SEED_TEST_PASSWORD` and the rest) and the dev seed key are set by
the AppHost for each run. You never set them by hand. `DevSeed__Key` is a random 32-byte value per run. It is
sent as the header `X-Dev-Seed-Key` and compared in constant time.

Nothing in this list is committed. The keys and passwords live in your user secrets, in the Docker container
environment for one run, or in the seeder's log.

## 7. Reset

To return to an empty database and seed again:

1. stop the AppHost (Ctrl+C);
2. remove the Postgres data volume:

   ```bash
   docker volume rm school-search-postgres-data
   ```

3. start again with `dotnet run --project src/SchoolSearch.AppHost`.

Removing the volume removes all local data: catalog rows added by you, reviews, billing rows and the accounts.
Authservice's `InitialAdmin` is created again because its database is new.

If you only want the accounts and demo data to be re-seeded while keeping the database, stop the AppHost, then run
it again. The seeder and the dev seed endpoints are idempotent, so nothing is duplicated.

## 8. Run without seeding

For one run, as an environment variable:

```bash
SeedDemo=false dotnet run --project src/SchoolSearch.AppHost
```

or, until you clear it, with user secrets:

```bash
dotnet user-secrets set "SeedDemo" "false" --project src/SchoolSearch.AppHost
```

With `SeedDemo=false` the catalog starts empty, the seeder does not run, the dev seed endpoints answer 404, and
authservice is off unless you set `IncludeAuthservice=true`. The web app still starts. Sign-in pages show the
unavailable state. This is the zero-credential run of `scripts/README.md`.

## 9. The authservice image

authservice runs from `ghcr.io/konradcinkusz/authservice:v0.3.4` (the tag is pinned in the AppHost, never `latest`).

- If the pull is denied (`denied` or `unauthorized` from `docker`), the GHCR package is private. Either:
  - log in: `docker login ghcr.io` with your GitHub user name and a personal access token that has the
    `read:packages` scope; or
  - make the package public in the GitHub package settings of `konradcinkusz/authservice`.
- The authservice README in that repository has the details of its own image and its settings. Read it when the
  container fails to start. Whether the package is public is an open point (`AUTHSERVICE.md` item G6).

## 10. Zero-Docker alternative: browse the UI only

If you only want to look at the web pages and you have no Docker, run the mock services instead. The mock
answers the catalog, the Reviews, Billing and agent routes with the same synthetic data, and has no accounts.

```bash
node tests/e2e/visual/mock-api.mjs            # catalog :8080, reviews :8082, billing :8083, agent :8084

CATALOG_API_URL=http://localhost:8080 \
REVIEWS_BASE_URL=http://localhost:8082 \
BILLING_BASE_URL=http://localhost:8083 \
AGENT_BASE_URL=http://localhost:8084 \
pnpm -C web/app dev
```

Then open the web app (port 3000 by default). Catalog, school, ranking and preschool pages work. The sign-in and
account pages show the unavailable state, because `AUTH_BASE_URL` is not set and the mock has no accounts. For
the details of the mock, see `tests/e2e/visual/README.md`.

## 11. Troubleshooting

| You see | Cause | What to do |
|---|---|---|
| The `seeder` resource is in a failed state, the rest runs | the seeder could not reach authservice, Reviews or Billing in time, or a call failed | the web app and the catalog still work. Read the seeder's console log for the failing step. Fix the cause (for example authservice still starting), then restart the `seeder` resource from the dashboard. It is idempotent |
| `429` in the seeder log, or sign-in refused | authservice rate-limits its auth endpoints | the seeder retries with backoff. If it still fails, wait a minute and restart the `seeder` resource |
| `Address already in use` or a port error | another process holds the port. authservice always uses `8090` | stop that process, or stop the other AppHost. The other resources get free ports from Aspire |
| `denied` or `unauthorized` when pulling authservice | the GHCR package is private (section 9) | `docker login ghcr.io` or make the package public |
| Sign-in fails with the password you expect | the password was generated in an earlier run and the authservice database still holds it | set `Seed:TestPassword` (section 5, option 2) and keep it, or reset the volume (section 7) |
| Reviews or Billing answer `404` on `/dev/seed` | `DevSeed__Enabled` is off, the key does not match, or the environment is Production | run the AppHost locally; never run the seed in Production |
| Tokens stop working after a restart | the signing key is ephemeral per run | sign in again, or set `Authservice:JwtPrivateKeyPem` |
| Scores or pupil counts look different from the demo notes | the import job wrote real data to `catalogdb` | reset the volume (section 7) and do not run the import against the demo database |

## Safety

- **Dev seeding is refused in Production.** The catalog demo seed is refused when the environment is Production
  (`src/SchoolSearch.Api/Demo/DemoSeedHostedService.cs`). The Reviews and Billing `/dev/seed` endpoints answer
  `404` unless `DevSeed__Enabled=true`, the environment is not Production, and the header `X-Dev-Seed-Key`
  matches. The Billing mock checkout is also off in Production (`Billing__MockMode`).
- **The per-run key.** `/dev/seed` requires the key that the AppHost generated for this run. A key from an
  earlier run does not work. The comparison is constant time.
- **All data is synthetic and labelled DEMO.** Names, addresses, sources, reviews, notices and enquiries are
  invented. Gmina, streets and schools are placeholders. The reserved `.test` domain is used for the emails.
  The text of sponsored profiles and enquiries is marked as sponsored or DEMO.
- **No real data and no secrets in the repository.** No password literal, no signing key and no API key is in
  any committed file. The seeder logs the generated password once, to the local dashboard only. The `.env` file
  and user secrets are ignored by git (`.gitignore`), and the secret scanner runs on every commit.
- **Do not import real data into this database.** The demo database is for demo data only.

## What is not verified

Written without a .NET SDK and without Docker. Nothing below has been run.

- The AppHost wiring for `SeedDemo`, `IncludeAuthservice`, the generated key, the seeder resource and the
  environment pass-through. The AppHost in the tree at the time of writing has no `SeedDemo` yet.
- The `/dev/seed` endpoints in Reviews and Billing, their request and response shapes, and their idempotency.
- The seeder (`src/SchoolSearch.Seeder`) and its unit tests (`tests/SchoolSearch.Seeder.Tests`). They are not
  compiled or run here. The unit tests do not use the network. The AppHost integration is not covered by CI.
- The authservice container: its pull (GHCR access), its first start, its migrations and its roles.
- Sign-in, roles and the owner, moderator and admin pages with the seeded accounts, end to end.
- Behaviour of a generated password across runs when the authservice database is kept (section 11).
- The stated Aspire resource names and endpoint names in section 3.

Owner action: the first real run (`dotnet run --project src/SchoolSearch.AppHost`) and the walk through section 5
are the checks. Record what fails in the ADR (`docs/adr/0019-local-seeding-and-test-accounts.md`), not here.
