# ADR-0019: Local seeding and test accounts, orchestrated by the AppHost

- **Status:** Accepted
- **Date:** 2026-10-13
- **Deciders:** author of the change (Konrad Cinkusz)

## Context

A developer who runs `dotnet run --project src/SchoolSearch.AppHost` today gets empty databases. The catalog has no
demo data unless `SCHOOLSEARCH_DEMO_SEED=true` is set by hand, Reviews and Billing have no data, and authservice is
off by default (ADR-0015, `docs/runbooks/AUTHSERVICE.md` section 5). No account exists, so the signed-in pages
(account, owner school console, moderation console, DSA notices) cannot be browsed locally. Reviewing the product as
a user, in each role, needs all of this at once.

The constraints that shape the answer:

- **Database per service** (ADR-0007, ADR-0013, the AppHost comment): each service owns its database. Nothing writes
  into another service's tables.
- **Identity is authservice alone** (ADR-0004, ADR-0015). This repository validates tokens and mints none. The user
  id is the JWT `sub`, and the Reviews and Billing services store a keyed hash of it, not the id.
- **Subject hashes are keyed** (`Reviews__SubjectPepper`, `Billing__SubjectPepper`). A seed that writes rows must
  compute them with the same pepper and the same code path as normal writes.
- **Nothing synthetic may reach Production.** The demo catalog is already refused in Production
  (`DemoSeedHostedService`). Any new seed path needs the same guard and a second one.
- **No secret in the repository** (`scripts/README.md`, `secrets.env.example`, the secret scanner).
- **Zero-credential start** (P8): the run without a seed must still start.

## Decision

The AppHost orchestrates local seeding. It is switched on by one flag, `SeedDemo` (default `true` in the AppHost).
With it on:

1. **Catalog demo seed.** The AppHost sets `SCHOOLSEARCH_DEMO_SEED=true` on the API. The existing demo seed
   (`src/SchoolSearch.Api/Demo`) runs on start.
2. **Per-service dev seed endpoints.** Reviews and Billing each expose `POST /dev/seed`. The endpoint is idempotent
   with deterministic ids. It takes the authservice subjects of the test accounts and writes the data through the
   service's own code and pepper. It answers `404` unless all three hold: `DevSeed__Enabled=true`, the environment is
   not Production, and the header `X-Dev-Seed-Key` matches `DevSeed__Key` (constant-time compare).
3. **A one-shot seeder.** `src/SchoolSearch.Seeder` is a console project that the AppHost runs as the resource
   `seeder`, after authservice, Reviews and Billing are healthy, and only when `SeedDemo` and authservice are both on. It creates the four test accounts (`parent`,
   `parent2`, `owner`, `moderator`) through authservice's public API (`register`, or `login` when the account exists),
   reads each `sub` from the token, assigns `Admin` to the moderator through authservice's admin API, then calls
   `/dev/seed` on Reviews and Billing with those subjects. It prints a summary and exits with code `0` (seeded),
   `1` (a seeding step failed or was cancelled) or `2` (configuration missing or invalid). It is idempotent.
   The seeder never writes to any database.
4. **Stored local values (amended 2026-10-13, before the first run).** The generated demo values are kept in one
   gitignored local file, `.local/seed-secrets.json` (keys: `testPassword`, `devSeedKey`, `reviewsPepper`,
   `billingPepper`, `jwtPrivateKeyPem`), so that they are stable across runs. Each value resolves in this order:
   explicit configuration (user secrets or environment), then the file, then a new value generated and written to
   the file. The file is written with mode `0600` on Unix. The reason for storing is that the authservice, Reviews
   and Billing databases persist in the Docker volume `school-search-postgres-data`: a per-run password or pepper
   would break sign-in and the author hashes. The `DevSeed__Key` (32 random bytes) is stored the same way and sent
   as the header `X-Dev-Seed-Key`. The test password is `Seed:TestPassword` when it is set in user secrets; otherwise
   the stored value, which is the same on every run. The seeder prints it in its log on every run when it was not
   set in configuration. No password literal is committed. The admin account (`Seed:AdminEmail`, default
   `admin@school-search.test`) is created by authservice's `InitialAdmin` setting, from the same password, with the
   role `SuperAdmin`. The moderator has the role `Admin`.
5. **Stored signing key (amended 2026-10-13).** When `Authservice:JwtPrivateKeyPem` is absent, the AppHost takes the
   RS256 key from the same local file, or generates an RSA 2048 key (PKCS#8, one line with literal `\n`) once and
   stores it there. A key from user secrets is used as given. The key is local-dev only and is never the production
   key. Reset: delete `.local/seed-secrets.json` and the Docker volume `school-search-postgres-data` together
   (`docs/runbooks/LOCAL-DEV.md` section 7).
6. **Test emails on the reserved `.test` TLD**, so no address can belong to a real person.
7. **Fixed seed contents.** Reviews and Billing write fixed ids and fixed acknowledgement tokens, so the demo
   notices can be opened by hand. These values are public in the source and are for local use only. The contents
   are listed in `docs/runbooks/LOCAL-DEV.md` section 4.

With `SeedDemo=false` none of this runs: no seeder, no local file, no authservice unless `IncludeAuthservice=true`,
the dev seed endpoints answer `404`, the catalog starts empty, and the Reviews and Billing peppers are random per
run.

All data is synthetic and labelled DEMO, in Polish, with no pupil names and no real school names.

## Options considered

| Option | For | Against |
|---|---|---|
| **AppHost-orchestrated seeding: a flag, per-service `/dev/seed` with a generated key, a one-shot seeder through authservice's public API (chosen)** | Each service writes its own data through its own code and pepper, so subject hashes and invariants hold. Accounts are created the way a user creates them, so the tokens and roles are real. The guard is in each service. Removing it is one flag | Four moving parts. Three new surfaces to guard (two endpoints, one job). Needs authservice's registration and admin API to behave as its README says, which is not yet verified |
| SQL scripts run into `catalogdb`, `reviewsdb`, `billingdb` and `authservicedb` | Simple, no code in the services | Breaks database per service: the AppHost would write into every database. It must compute the keyed subject hashes itself, so it duplicates the pepper logic and can drift from it. It must write authservice's password hashes and role rows, which belong to authservice. Rejected |
| Seeding directly from the AppHost (it calls the services' repositories or writes rows) | One place | The AppHost references no service data code and would have to. It would bypass the services' validation and the Production guard. Rejected |
| A fixed test password committed to the repository | Predictable, no printing | A credential in git. The secret scanner would flag a literal, and a copied password reaches every clone. Rejected: the password is generated and stored locally, or set in user secrets by the developer |
| Use the mock services only (`tests/e2e/visual/mock-api.mjs`) | No Docker, no accounts needed for layout work | No accounts, no roles, no owner or moderator flows, and not the real services. Kept as the zero-Docker alternative for UI-only browsing |

## Consequences

**Easier.** One command gives a seeded, signed-in environment in every role. The same seed serves manual browsing,
the visual harness and later end-to-end work. Each service stays the only writer of its database. Reset is one
Docker volume.

**Harder.** The local stack now has a dependency chain: authservice must be healthy, then the seeder runs. A failure
in the seeder leaves the rest running, so the failure is easy to miss. It must be read in the dashboard. The
Reviews and Billing services gain a route that writes data, guarded by configuration. Two of its guards must hold
(the environment check and the key), and a test must prove each.

**Secrets on disk, local only.** The stored values include the authservice RS256 private key, the test password,
the dev seed key and the two peppers. They sit in `.local/seed-secrets.json`. The file is gitignored and mode `0600`
on Unix, and it holds no production value. This is a deliberate exception to a file-free local setup. It is
acceptable under the rule "no secrets in committed files" (AGENTS.md, `scripts/README.md`) because nothing in it is
committed, and it is the only way the persisted databases stay usable across runs. Anyone who copies the file
copies the local demo credentials.

**Reset is two steps.** The file and the Docker volume must be removed together. Removing only the file breaks
sign-in.

**Checks owed, not done.** At the time of writing, no .NET SDK and no Docker were available, so:

- the Reviews and Billing `/dev/seed` tests (in `tests/SchoolSearch.Reviews.Tests` and `tests/SchoolSearch.Billing.Tests`)
  and the seeder's unit tests (`tests/SchoolSearch.Seeder.Tests`) are written but not compiled or run;
- the AppHost integration is not covered by CI. It is checked only by a local run;
- the Production refusal is proven by a test, not yet by a run against a Production-like configuration.

**Exit conditions and re-decisions.**

- Re-decide the seeder's use of authservice's API if authservice changes its register, login or role endpoints.
  The contract is in `docs/runbooks/AUTHSERVICE.md` section 7 and the README of the authservice repository.
- Re-check the stored-value behaviour when the local store, `Seed:TestPassword` or the Docker volume changes
  independently. The file and the volume must match. A changed `Seed:TestPassword` does not change a password the
  authservice database already holds, and sign-in then fails. The documented answers are in
  `docs/runbooks/LOCAL-DEV.md` sections 7 and 11.

## Open points

1. **GHCR pull access.** `ghcr.io/konradcinkusz/authservice:v0.3.4` is private by default (`AUTHSERVICE.md` G6). A
   local run needs either `docker login ghcr.io` with a token that has `read:packages`, or the package made public.
   The owner decides which. Not verified on this machine.
2. **A fixed-password option.** Today the password is generated once and kept in the local store, or fixed through the developer's own user secrets.
   The owner may want a documented fixed password for shared demos. That would be a choice made outside the
   repository, never a literal in it. Decision for the owner.
3. **Seeding the MCP service.** The MCP service owns no database and reads the catalog and reviews over HTTP. This
   ADR does not seed it. The agent is off without `ANTHROPIC_API_KEY`, and its tools need no accounts. Whether
   the MCP service needs seeded data of its own (for example an example conversation) is open.
4. **The AppHost's ports.** Authservice is fixed at `8090` (`AuthBaseUrl` in the AppHost). A process already on that port
   would make authservice fail to start; this was not run. Whether the port should move to an Aspire-assigned one is
   open, because the consumers' `Auth__Authority` and the web's `AUTH_BASE_URL` are set from that constant.
5. **Persisted state between runs.** The Reviews and Billing peppers, the dev seed key, the test password and the
   authservice signing key are kept in `.local/seed-secrets.json` (decision 4). Rows written with one pepper stay
   in place after a restart with another, so a changed value in configuration is not safe once data exists. The
   owner decides whether a documented fixed value for shared demos should be provided; that choice would live
   outside the repository (open point 2).
