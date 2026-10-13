# Runbook: local development with seeded demo data

This runbook starts the whole product on your machine with one command and signs you in as a test account
in each role. It is for local development only. It is not the production topology (that is `flyio/` and the
tag-driven workflow, see `docs/runbooks/AUTHSERVICE.md`).

Every record that this setup creates is synthetic and labelled DEMO. No real school, pupil, parent, address
or credential is involved. See [Safety](#safety).

> **Status of this document.** It describes what the code in the tree does (ADR-0019). The code was written
> without a .NET SDK or Docker on the machine that wrote it, so it has not been compiled or run there. The first
> real run is the check. See [What is not verified](#what-is-not-verified) and [Known limitations](#known-limitations).

## 1. Prerequisites

| Tool | Why | Check |
|---|---|---|
| .NET 10 SDK | builds and runs the AppHost and every service (`global.json` pins the band) | `dotnet --version` starts with `10.` |
| Docker (Docker Desktop, Colima or Podman with the Docker API) | Aspire starts Postgres, and authservice when enabled, as containers | `docker info` answers |
| Node.js and pnpm | the web app in `web/app` | `node --version`, `pnpm --version` |

No other tool is needed. The AppHost generates the signing key and the secrets of the demo itself (section 6).

Optional: run `./scripts/setup.sh` (or `scripts\setup.ps1` on Windows) once per clone. It checks the tools and
writes `dotnet user-secrets` for the optional values. `./scripts/setup.sh --check` lists what is missing and
changes nothing. Setup is not required for the seeded run.

## 2. Start everything

```bash
dotnet run --project src/SchoolSearch.AppHost
```

That is the one command. With the defaults (`SeedDemo` is `true`), it:

1. starts a Postgres container with the databases `catalogdb`, `reviewsdb` and `billingdb`, and `authservicedb`
   when authservice is on. Data lives in the Docker volume `school-search-postgres-data`;
2. starts the catalog API, the Reviews service, the Billing service and the MCP service;
3. reads the demo secrets from `.local/seed-secrets.json`, or generates them and writes them there (section 6);
4. starts authservice as a container on `http://localhost:8090` (because `SeedDemo` turns it on), with the RS256
   signing key from that file, or from `Authservice:JwtPrivateKeyPem` when you set it;
5. starts the one-shot `seeder` after authservice, Reviews and Billing are healthy. It creates the test
   accounts and loads the demo data (section 4), then exits. It runs only when `SeedDemo` and authservice are on;
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
| `seeder` | one-shot job that creates accounts and calls the dev seed endpoints. It shows as finished | its **console logs**: the summary and the test password (section 5) |
| `web` | the Next.js product surface | endpoint `http` (the port is assigned by Aspire; the dashboard shows it) |

Ports are assigned by Aspire, except authservice (fixed at `8090`). Use the dashboard to find the web address.
Open the web app at its `http` endpoint, for example `/` for the catalog and `/konto/logowanie` to sign in.

## 4. What is seeded

Seeding runs in three places: the catalog API (on its own start), the Reviews and Billing services (through
`POST /dev/seed`), and the seeder (accounts). All seeded data is idempotent: running the seeder again creates
nothing new. Each service recognises its demo rows by fixed ids.

**Catalog** (`src/SchoolSearch.Api/Demo`, flag `SCHOOLSEARCH_DEMO_SEED=true`, which the AppHost sets from `SeedDemo`):

- the gmina **Przykładowo** (TERYT `9999999`), with a street rule set for the street *Przykladowa*;
- schools `demo-sp-1` to `demo-sp-14` (for example `demo-sp-1` is published, `demo-sp-3` has too few pupils to
  publish a score, `demo-sp-6` has no exam data);
- five demonstration preschools (`przedszkole-demonstracyjne-nr-1` to `-nr-5`).

Try `/?gmina=9999999&ulica=Przykladowa&numer=5` for a catchment result, `/szkoly/demo-sp-1` for a school page,
and `/ranking/gmina:9999999` for a ranking.

**Reviews, DSA and moderation** (`POST /dev/seed` on Reviews, written by the seeder with the parent, parent2,
owner and moderator subjects). Six reviews, all on `demo-sp-1`, `demo-sp-2` and `demo-sp-3`:

- 4 published reviews from the two parents (`parent` and `parent2`), with different ratings and themes;
- 1 review waiting for moderation (`pending`), by `parent`;
- 1 review removed by a moderation decision, by `parent2`, with its statement of reasons (DSA);
- 1 owner reply to a published review, under the label of the school management (not a real person);
- 3 DSA notices: one open, one decided with removal (appealed, see the complaint below), one decided with keep
  (about content that is not a review, a link on `example.test`);
- 1 complaint against the removal decision, in the received state;
- 2 review-triage rows: labels only (`needs_attention` and `unclear`). No AI model is called. The labels order the
  moderator queue and are never shown to readers.

The removal and the keep decisions go through the moderation service, so each carries `Automated=false` and a
statement of reasons, as a real decision does. Reviews never change a school's score.

**Billing and owner data** (`POST /dev/seed` on Billing, mock mode, written for the owner and the moderator):

- 2 school claims by the owner: an **approved** claim on `demo-sp-1` (decided by the moderator) and a **pending**
  claim on `demo-sp-2`, for the moderator's queue;
- 1 **active** mock subscription on `demo-sp-1`, with its period ending 30 days after the seed. Its provider
  reference marks it as a mock row;
- 1 **published** sponsored profile on `demo-sp-1`, labelled as sponsored and DEMO;
- 4 synthetic parent enquiries on `demo-sp-1` in the owner's inbox, from addresses on `example.test`: 3 in the
  `new` state and 1 in the `read` state. The only states are `new` and `read`; there is no "answered" state.

Billing never changes a school's score.

### Fixed demo tokens (local only)

The Reviews seed uses fixed ids and acknowledgement tokens (`DevSeedIds.cs`), so you can open the demo notices
without looking anything up. They are demo values that are public in the source code. Use them on your machine
only; they mean nothing in any other environment.

| Item | Id | Token | State |
|---|---|---|---|
| notice on the open review | `7e5d0003-0000-4000-8000-00000000b001` | `DemoAckNoticeOpen00001` | open |
| notice on the removed review | `7e5d0003-0000-4000-8000-00000000b002` | `DemoAckNoticeDone00002` | decided: removal, appealed |
| notice on the content that is not a review | `7e5d0003-0000-4000-8000-00000000b003` | `DemoAckNoticeKeep00003` | decided: keep |
| complaint against the removal | `7e5d0004-0000-4000-8000-00000000c001` | `DemoAckComplaint000004` | received |

The notice status page is `/zglos-tresc`: enter the notice id and its token there. No page in this tree reads a complaint by its token, so the complaint token is
recorded for completeness only.

## 5. Test accounts

The seeder creates these accounts through authservice's public API (`register`, then `login` when the account
already exists). It assigns the roles through authservice's admin API.

| Email | Role in authservice | What it is for | What to open |
|---|---|---|---|
| `admin@school-search.test` (`Seed:AdminEmail`) | `SuperAdmin` (created by authservice's initial-admin setting) | the administrator of the whole product | everything the moderator can open, plus the authservice admin API (roles, lock, unlock) |
| `moderator@school-search.test` | `Admin` | moderation: review queue, DSA notices and statements of reasons, school corrections, claims | `/moderacja` |
| `parent@school-search.test` | `User` | a parent: writes reviews, files a notice about content | a school page such as `/szkoly/demo-sp-1` (write a review there), `/zglos-tresc` (notice), the account pages under `/konto` |
| `parent2@school-search.test` | `User` | a second parent, for the second set of reviews and the removed review | the same pages as `parent` |
| `owner@school-search.test` | `User` | a school owner, with an approved claim on `demo-sp-1` (active sponsored profile) and a pending claim on `demo-sp-2` | `/konto/szkola` (the owner's schools), `/konto/szkola/demo-sp-1` (sponsored profile, enquiries inbox) |

All five accounts use the same password, `Seed:TestPassword` (or the generated test password, see below). The
`admin` account is created by authservice from the same password, so one password signs you in everywhere.
The moderator has the role `Admin`; the admin has `SuperAdmin`. The other three have `User`.

The role appears in the access token. An account with the role `Admin` or `SuperAdmin` passes the API's admin
group (ADR-0015). A `User` does not.

### How to get the password

There are two ways. The first is the default.

1. **Generated once and stored (the default).** When `Seed:TestPassword` is not set, the AppHost uses the test
   password in `.local/seed-secrets.json`, and generates and stores one the first time (section 6). The password
   is the same on every run, because the authservice database keeps its hash. The seeder prints it in its
   console log on **every run**, in the `seeder` resource log in the Aspire dashboard. The printed line is
   `Password for every account above: ...`.
2. **Fixed, from your user secrets.** Set `Seed:TestPassword` once. The seeder and authservice use it, and the
   seeder does not print it:

   ```bash
   dotnet user-secrets set "Seed:TestPassword" "<a password of your choice>" --project src/SchoolSearch.AppHost
   ```

   Set it before the first run, or keep it the same afterwards. A changed value does not change a password that
   the authservice database already holds (see section 11). Do not put the value in a file in the repository.

## 6. Flags and secrets

Set them with `dotnet user-secrets` on the AppHost project (the usual way), or as environment variables for one run.

| Setting | Default | What it does |
|---|---|---|
| `SeedDemo` | `true` | master switch: catalog demo seed, the seeder, authservice, the dev seed endpoints and the local store. Set `false` to run without seeding (section 8) |
| `IncludeAuthservice` | follows `SeedDemo` | run authservice as a container. Set it `true` to have authservice without the demo seed |
| `Authservice:JwtPrivateKeyPem` | absent: taken from the local store | RS256 private key for authservice, PKCS#8 PEM on one line with literal `\n`. When set, it wins over the store. Generate one as in `docs/runbooks/AUTHSERVICE.md` section 5, never reuse the production key |
| `Seed:AdminEmail` | `admin@school-search.test` | the email of the SuperAdmin account |
| `Seed:TestPassword` | taken from the local store, generated on first run | the password of all test accounts (section 5). When set, it wins over the store |
| `Reviews:SubjectPepper` | taken from the local store | keys the author hashes of the Reviews service. When set, it wins over the store |
| `Billing:SubjectPepper` | taken from the local store | keys the subject hashes of the Billing service. When set, it wins over the store |
| `ANTHROPIC_API_KEY` | absent | passed to the API, Reviews, Billing and MCP when present. Without it the AI features answer `503 ai_unavailable` and the rest works |
| `AI_ENABLED` | absent (off) | the switch for the AI features. Set `true` together with the key |

Example for the AI features:

```bash
dotnet user-secrets set "ANTHROPIC_API_KEY" "<your key>" --project src/SchoolSearch.AppHost
dotnet user-secrets set "AI_ENABLED" "true" --project src/SchoolSearch.AppHost
```

The seeder's own variables (`DEV_SEED_KEY`, `SEED_TEST_PASSWORD` and the rest) are set by the AppHost for each run.
You never set them by hand. The dev seed key (`DevSeed__Key`) is 32 random bytes, generated once and stored in the
local store. It is sent as the header `X-Dev-Seed-Key` and compared in constant time. `DevSeed__Enabled` is set by
the AppHost when `SeedDemo` is on.

### The local store: `.local/seed-secrets.json`

The demo's generated values are kept in one local JSON file, `<repo>/.local/seed-secrets.json`. The authservice,
Reviews and Billing databases persist in the Docker volume `school-search-postgres-data`, so the values they were
written with must come back on the next run. A new random password or pepper per run would break sign-in and
the author hashes.

```json
{
  "testPassword": "<generated>",
  "devSeedKey": "<generated>",
  "reviewsPepper": "<generated>",
  "billingPepper": "<generated>",
  "jwtPrivateKeyPem": "<generated, RSA 2048 PKCS#8 on one line with literal \\n>"
}
```

- **Location and git.** The directory `.local/` is in `.gitignore`. The file is never committed.
- **Permissions.** On Unix the file is set to mode `0600` after it is written. Windows does not apply that mode.
- **Resolution, per value.** Configuration (user secrets or environment) first; then the file; then a new value,
  which is written to the file. A value set in configuration is used for the run, and the file is not changed
  for it.
- **The signing key is in this file too.** The RS256 key for authservice is local-dev only. It is the one place
  where a private key is stored in the working tree, and it is acceptable only because the file is gitignored and
  holds no production value (ADR-0019, consequence "no secrets in committed files").
- **Unreadable file.** If the file exists but cannot be parsed, it is never overwritten. The values generated in
  that run last for that run only, and the startup banner says so.
- **Sensitive.** The file holds the local signing key and the passwords. Do not copy it into the repository, a
  ticket or a chat.

## 7. Reset

To return to an empty database and new demo values, remove the local store **and** the Postgres data volume
together:

1. stop the AppHost (Ctrl+C);
2. delete the local store:

   ```bash
   rm .local/seed-secrets.json          # Windows: Remove-Item .local\seed-secrets.json
   ```

3. remove the Postgres data volume:

   ```bash
   docker volume rm school-search-postgres-data
   ```

4. start again with `dotnet run --project src/SchoolSearch.AppHost`.

Do both steps together. Deleting only `.local/seed-secrets.json` breaks login: the database still holds the old
password hash and the old subject hashes, and the new values do not match them. Deleting only the volume is safe,
but it reuses the values in the file, so it does not give you new secrets.

Removing both removes all local data: catalog rows added by you, reviews, billing rows and the accounts. The
authservice `InitialAdmin` is created again because its database is new. The generated values are new too.

If you only want the accounts and demo data to be seeded again while keeping the database, stop the AppHost and run
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

With `SeedDemo=false` the catalog starts empty, the local store is not used, the seeder does not run, the dev seed
endpoints answer 404, and the Reviews and Billing peppers are random for each run. authservice is off unless you
set `IncludeAuthservice=true`. In that case the AppHost needs the signing key in user secrets as
`Authservice:JwtPrivateKeyPem`; without it the AppHost stops with a message that names the missing setting. The
web app still starts. Sign-in pages show the unavailable state when authservice is off. This is the
zero-credential run of `scripts/README.md`.

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
| Sign-in fails with the password you expect, or the seeder says `already exists but signing in failed` | the password in the local store (or `Seed:TestPassword`) is not the one the authservice database holds. Typical causes: `Seed:TestPassword` was changed after the first run, the store file was deleted or restored from another clone, or the database is from an earlier run | put the value back (remove the changed `Seed:TestPassword`, or restore the file), or reset both the store and the volume (section 7) |
| The startup banner says `Seeder: skipped` | `SeedDemo` is on but authservice is not included (`IncludeAuthservice=false`), so the test accounts cannot be created. No demo data is written to Reviews or Billing either, because only the seeder calls their seed endpoints | unset `IncludeAuthservice` or set it to `true`, or set `SeedDemo=false` |
| Reviews or Billing answer `404` on `/dev/seed` | `DevSeed__Enabled` is off, the key does not match, or the environment is Production | run the AppHost locally; never run the seed in Production |
| Seeder reports a Reviews or Billing error after a first run | the Reviews seed stores its base rows before the moderation steps, so a failed moderation step leaves some demo rows missing. A rerun then finds the marker and writes nothing | reset both the store and the volume (section 7) |
| Tokens stop working after a restart | the signing key changed: the local store was deleted, or `Authservice:JwtPrivateKeyPem` changed | sign in again |
| Scores or pupil counts look different from the demo notes | the import job wrote real data to `catalogdb` | reset the volume (section 7) and do not run the import against the demo database |

## Safety

- **Dev seeding is refused in Production.** The catalog demo seed is refused when the environment is Production
  (`src/SchoolSearch.Api/Demo/DemoSeedHostedService.cs`). The Reviews and Billing `/dev/seed` endpoints answer
  `404` unless `DevSeed__Enabled=true`, the environment is not Production, and the header `X-Dev-Seed-Key`
  matches. The Billing mock checkout is also off in Production (`Billing__MockMode`).
- **The dev seed key.** It is generated once and kept in `.local/seed-secrets.json`, so it is the same on every run.
  The comparison is constant time.
- **All data is synthetic and labelled DEMO.** Names, addresses, sources, reviews, notices and enquiries are
  invented. Gmina, streets and schools are placeholders. The reserved `.test` and `example.test` domains are used
  for the emails and links. The text of sponsored profiles and enquiries is marked as sponsored or DEMO.
- **No real data and no secrets in the repository.** No password literal, no signing key and no API key is in any
  committed file. The generated values live only in `.local/seed-secrets.json` (gitignored), in your user secrets,
  or in the AppHost's environment for one run. The seeder prints the test password in its log on each run, to the
  local dashboard only. The `.env` file and user secrets are ignored by git (`.gitignore`), and the secret scanner
  runs on every commit.
- **Do not import real data into this database.** The demo database is for demo data only.

## What is not verified

Written without a .NET SDK and without Docker. Nothing below has been run from this change.

- The AppHost wiring for `SeedDemo`, `IncludeAuthservice`, the local store, the generated signing key, the seeder
  resource and the environment pass-through.
- The `/dev/seed` endpoints in Reviews and Billing, their request and response shapes, and their idempotency.
- The seeder (`src/SchoolSearch.Seeder`) and its unit tests (`tests/SchoolSearch.Seeder.Tests`). The unit tests are
  in `SchoolSearch.sln`, so `dotnet test SchoolSearch.sln` in CI runs them. They do not use the network. No result
  from this change has been seen.
- The AppHost integration is not covered by CI. It is checked only by a local run.
- The authservice container: its pull (GHCR access), its first start, its migrations and its roles.
- Sign-in, roles and the owner, moderator and admin pages with the seeded accounts, end to end.
- The stated Aspire resource names and endpoint names in section 3.

## Known limitations

- **Not compiled or run in the authoring environment.** The code exists in the tree, but it was never built or started
  where this runbook was written. Treat section 2 as the expected behaviour until the first run confirms it.
- **The AppHost integration is not covered by CI.** Only unit and service tests run in CI.
- **The ghcr pull.** The authservice image is pinned to `v0.3.4` on GHCR. If the package is private, the run stops at
  the pull (section 9).
- **Ephemeral data if the store is deleted.** The local store and the Docker volume are the only places the demo's
  data and secrets live. Deleting the store alone breaks sign-in; deleting both is a full reset (section 7).
- **The Aspire dashboard shows environment values.** For local use, the dashboard shows the environment of each
  resource, including the test password and the seed values passed to authservice, the seeder and the services.
  Do not share a dashboard screenshot.
- **No "answered" enquiry state.** The Billing enquiry states are `new` and `read` only. The demo has no answered
  enquiry.
- **A partial Reviews seed is not repaired by a rerun** (section 11).

Owner action: the first real run (`dotnet run --project src/SchoolSearch.AppHost`) and the walk through section 5
are the checks. Record what fails in the ADR (`docs/adr/0019-local-seeding-and-test-accounts.md`), not here.
