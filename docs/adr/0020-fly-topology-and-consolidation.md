# ADR-0020: Fly topology: one Postgres app, one app per service, and why they are not consolidated

- **Status:** Accepted (the topology); Open (the Postgres data-safety gaps in section "Postgres risks", owner decision)
- **Date:** 2026-10-14
- **Deciders:** owner question "can everything go into two Fly apps, one for Postgres and one for the API?", answered here from the repository's own standards. Nothing in this ADR was executed against Fly.

## Context

The owner asked whether the estate can be reduced to two Fly apps: one for Postgres and one for "the API". Today the estate is
seven deployable apps plus one one-shot job:

| Fly app | What it is | Own database | Pinned (`min_machines_running`) |
|---|---|---|---|
| `school-search-postgres` | vanilla `postgres:17-alpine`, one 10 GB volume, no public listener | holds all four | n/a (never auto-stopped) |
| `school-search-authservice-dev` | external identity provider image (`konradcinkusz/authservice`, pinned tag), only holder of a signing key | `authservicedb` | 1 (every validator fetches its JWKS in-request) |
| `school-search-api-dev` | catalog API, scoring, admin | `catalogdb` | 1 (web and mcp call it in-request) |
| `school-search-reviews-dev` | user-generated reviews, DSA notices, moderation | `reviewsdb` | 1 |
| `school-search-billing-dev` | claims, enquiries, subscriptions, entitlement | `billingdb` | 1 |
| `school-search-mcp-dev` | read-only MCP server and the advisor agent | none | 0 |
| `school-search-web-dev` | Next.js BFF and site | none | 0 |
| (job) `school-search-import` | one-shot import, a machine, not an app | writes `catalogdb` | n/a |

Standards that bear on the answer:

- **P3 (service per bounded context, database per service).** "A service owns its schema. No second service opens a connection
  to it. Physical co-location is a cost decision and is allowed ... The logical boundary is what must not be crossed."
- **P7 (Fly topology is cost-shaped).** One Fly app and one `fly.toml` per service; `min_machines_running = 0` unless another
  service calls it in-request; shared-cpu machines.
- **P5.** Exactly one service holds a signing key, and it is an external image that is never vendored here.
- **P6.** One container per service. **P4.** The owning service migrates its own schema.
- Already decided here: ADR-0006 (MCP is its own service), ADR-0007 (Import and Api are two deployables of one context),
  ADR-0015 (Reviews is a separate deployable with its own database), ADR-0016 (Billing is a separate deployable).
  `tests/SchoolSearch.Architecture.Tests` enforces boundaries between them (identity, scoring must not reference billing).

## Decision

1. **Keep the topology: one Postgres app and one Fly app per service.** The Postgres half of the owner's proposal is already
   true: there is exactly one Postgres app. The "one app for the API" half is not adopted.
2. **Database per service stays logical.** One Postgres instance, four databases (`catalogdb`, `reviewsdb`, `billingdb`,
   `authservicedb`), created idempotently by the `ensure_databases` job of `.github/workflows/flyio.yml`.
3. **Cost is reduced with the levers in "Cost levers", not by merging services.**
4. **The Postgres data-safety gaps are stated, not hidden** (section "Postgres risks"). They are the owner's accepted risk
   until a backup and restore path exists.

## Why not "one app for the API"

Merging api + reviews + billing (+ authservice + mcp + web) into one app is not a deployment tweak. It is a change of design:

- **authservice cannot be merged at all.** It is an external, separately released image with the only signing key (P5, ADR-0015).
  Folding it into another process would mean vendoring its source or its binary, which the standards forbid.
- **web is a Node.js process** (Next.js standalone, port 3000). It is a different runtime and image from the .NET services.
- **api + reviews + billing (+ mcp) as one process is a modular monolith.** One image, one process, one scaling and failure
  domain, one release train, one set of secrets in one environment (the billing webhook secret and pepper next to the
  reviews pepper and the catalog connection). Each bounded context currently has its own image, its own migration owner,
  its own scaling profile and its own blast radius. Reviews are user-generated content that must never feed a score; billing
  holds payment-related records; the architecture tests are written around those separations.
- **Database per service would be violated or merely hidden.** In one process a single `DbContext` set could open all databases
  with one connection credential. That is exactly the shortcut P3 forbids, and the shared-role finding below shows how thin the
  boundary already is.
- **The savings are small.** Per `flyio/INFRASTRUCTURE-ANALYSIS.md`, a stopped machine costs roughly its rootfs only, and an
  always-on shared-cpu-1x 512 MB machine is estimated at about 3 to 6 per month **[estimate, not a quote]**. The three .NET
  services pinned for correctness (api, reviews, billing) are three such machines plus authservice. Merging them would save on
  the order of two machines **[estimate]** and cost the isolation above.

If the owner still wants the modular monolith, that is a new decision with its own ADR that supersedes ADR-0007, ADR-0015 and
ADR-0016, a refactor of project boundaries and the architecture tests, and a migration of three databases' ownership into one
service. It is not a Fly configuration change and this ADR does not recommend it.

## Cost levers (legitimate, in order of effect)

| Lever | State today | Note |
|---|---|---|
| Scale to zero where no in-request caller depends on the service | `web` and `mcp` already `min_machines_running = 0` | A cold start there is a slow first page or first agent call, not a failed call (Fly guide section 7) |
| Run `mcp` only when agent traffic exists | Already 0. Optionally do not deploy it at all until a client exists | Set the repository variable `MCP_ENABLED=false`: tag runs and `all` then skip mcp; web's `/doradca` shows its degraded state (P8). Flip it to `true` (or delete it) when the first agent client is onboarded |
| Shared-cpu small machines | Every app is `shared-cpu-1x`, 512 MB (Postgres 1 GB) | Postgres to 512 MB is deferred until measured (INFRASTRUCTURE-ANALYSIS section 4) |
| One Postgres machine | Already (`--ha=false`) | A second machine would be an empty volume, not a replica |
| Skip HA at launch | Already: every app is one machine | The cost is availability, not data (section "Postgres risks") |
| Unpin `reviews` / `billing` | Pinned, each with a named in-request caller (the web BFF) | Owner may lower to 0 at the price of a cold start on public pages (the billing toml says so) |
| Stop the whole estate when idle | `flyio-scale.yml` `stop` | `stop` destroys the stateless machines; `start` re-creates one per app. Postgres is never touched |

Not levers (off the table, see INFRASTRUCTURE-ANALYSIS section 7): turning off `force_https`, a public Postgres listener,
sharing one database between services, `auto_stop` on Postgres, `.internal` for service-to-service HTTP.

## Postgres risks (what the repository does today, and what is missing)

Verified by reading `flyio/postgres.fly.toml` and `.github/workflows/flyio.yml`. Nothing was run.

| Topic | Today | Missing |
|---|---|---|
| Image | vanilla `postgres:17-alpine` on a Fly Machine with a volume (`school_search_pgdata`, 10 GB, `PGDATA` in a subdirectory). Not Fly's managed Postgres | No managed operations: no automatic minor-version upgrades, no console, no managed failover |
| Network | no `[http_service]`, no `[[services]]`, no public IP; reached at `school-search-postgres.internal:5432` | n/a (correct) |
| Databases | `catalogdb` created by the image (`POSTGRES_DB`). `reviewsdb`, `billingdb`, `authservicedb` were previously "created once by hand" per the runbooks; the `ensure_databases` job now creates them idempotently over `flyctl ssh console` | That job is **unverified** until its first run; `SKIP_DB_BOOTSTRAP=true` restores the manual path |
| **Roles** | **One role, shared by all services**: `schoolsearch` (`POSTGRES_USER`), the superuser of the instance, with one password (`POSTGRES_PASSWORD`). Each service gets a connection string with its own database name but the same user and password | Separate roles per service with `CONNECT` revoked from the others. Today any service's credential can open every database, so P3's "no second service opens a connection" is held by convention and by code, not by the database |
| Backups | Fly's platform volume snapshots exist (schedule and retention are Fly's; **not configured or verified here**). The pipeline takes one extra best-effort snapshot before each Postgres redeploy | No `pg_dump`, no off-Fly copy, no WAL archiving or point-in-time recovery, no restore drill, no documented retention |
| Failover | none: one machine, one volume, one region (`waw`) | A replica or a restore-based recovery procedure with a stated recovery time |
| Volume | 10 GB, `initial_size` set; growing is possible, shrinking is not | Disk-full alerting |
| Password | `POSTGRES_PASSWORD` is read only on first boot of an empty volume; changing the Fly secret later does not change the database password | A documented rotation (`ALTER USER`) |

Consequence: the catalog can be re-imported from public sources; **reviews, billing records and accounts cannot**. Before real
users write data, the owner decides one of: accept the platform snapshots only; add a scheduled logical dump to storage outside
Fly (a new workflow and a storage account, not built here); or move to a managed Postgres (a new ADR, a change of P3 co-location
and of the cost model). Recorded as an accepted risk in the deviation register of `docs/architecture/00-ARCHITECTURE.md`.

## Options considered

| Option | For | Against |
|---|---|---|
| **A. One Postgres app + one app per service (chosen)** | Matches P3, P5, P6, P7 and ADR-0006/0007/0015/0016; independent scaling, release and blast radius; the architecture tests hold | More apps to operate; four always-on machines (estimate) |
| B. Two apps: Postgres + one merged API process | Fewer machines to pay for and to deploy | Modular monolith (design change); authservice and web cannot join it anyway; shared failure domain; database-per-service violated or hidden; supersedes three ADRs |
| C. A Postgres instance per service | Strictest reading of P3; separate availability targets | About three more always-on machines and volumes **[estimate]**; no benefit while one team and one availability target |
| D. Fly managed Postgres or an external managed Postgres | Backups, failover and upgrades as a service | New cost line, not estimated here; outside P7's vanilla-image decision; needs its own ADR |
| E. Separate Postgres roles per service, same instance | Enforces P3 in the database at no machine cost | Needs a role-creation job, per-service passwords and changed connection strings; not built here (see Consequences) |

## Consequences

- The estate stays at seven apps. The deploy order is postgres, databases, authservice, api, reviews, billing, mcp, web
  (`flyio.yml`); every job checks all of its upstream results.
- **Open, owner decision:** option E (per-service roles) and a backup path (section "Postgres risks"). Exit conditions mirror the
  deviation register rows of ADR-0015/0016: a second service with its own availability target, or a restore requirement the
  single volume cannot meet. The data-safety decision should be taken **before** reviews or billing hold real user data.
- Revisit this ADR if the measured idle cost of the always-on machines becomes the binding constraint; the first action is
  lowering `reviews`/`billing` to 0 with a documented cold-start cost, not merging services.
- All cost figures here are estimates quoted from `flyio/INFRASTRUCTURE-ANALYSIS.md`. No price was looked up for this ADR.
