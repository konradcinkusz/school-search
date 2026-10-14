# Infrastructure analysis: school-search on Fly.io

Scope: the Fly topology in `docs/MASTER-PROMPT.md` (region `waw`, GHCR images, `--image` deploys).
It answers Fly §13's four questions (`docs/guides/FLY-IO-DEPLOYMENT.md`, section 13). Costs are
estimates, not quotes. Every cost figure is marked **[estimate]** and lists its assumptions in section 6.

Nothing in this document has been executed. No `flyctl` run, no deploy, no measurement. Every
cold-start number is a budget or an estimate until a real machine has been timed.

## 0. Topology at a glance

| App | Shape | Phase | `min_machines_running` | Volume | Public listener | Reached by |
|---|---|---|---|---|---|---|
| `school-search-postgres` | database | A | n/a (never auto-stopped) | 10 GB, `--ha=false` | **none** | `school-search-postgres.internal:5432` (api, import) |
| `school-search-api-dev` | HTTP service | A | **1** | none | yes (`.fly.dev`) | web (in-request), mcp (in-request) |
| `school-search-web-dev` | frontend | A | 0 | none | yes | browsers |
| `school-search-mcp-dev` | HTTP service | later (MCP) | 0 | none | yes | external agents |
| `school-search-import` (one-shot) | `fly machine run` | B | none (runs and exits) | none | none | run by CI, reads postgres |
| `school-search-authservice-dev` | HTTP service | E | **1** | none | yes | api, reviews, billing (JWKS, in-request) |
| `school-search-reviews-dev` | HTTP service | E | **1** | none | yes | web BFF (in-request), mcp |
| `school-search-billing-dev` | HTTP service | F | **1** | none | yes | web BFF (in-request) |

Update 2026-10-14: the table now lists every deployable app. The reasoning for why these are separate apps, why one Postgres
app holds four logical databases and why they are not merged into "one app for the API" is `docs/adr/0020-fly-topology-and-consolidation.md`.
At launch with everything on the estate idles on **five running machines** (postgres, api, authservice, reviews, billing);
web and mcp are stopped. Cost figures in section 6 predate reviews and billing: add two always-on 512 MB machines
**[estimate]** at the same per-machine range.

Notes on the table:
- The import job runs as a machine of the **api app** (see section 5), not as a separate app. Its
  secrets come from the same app secret store.
- Only one volume exists in the estate: postgres. The other apps are stateless, so they can be replaced,
  restarted or scaled without data risk.

## 1. What runs when nothing is happening?

Assume no user traffic, no agent traffic, and no import running.

| Machine | State while idle | RAM | Always-on? | What it costs when idle |
|---|---|---|---|---|
| postgres | running | 1 GB | yes, it has no auto-stop | full machine price, plus the 10 GB volume |
| api (1 pinned) | running | 512 MB | yes (`min_machines_running = 1`) | full machine price |
| web | stopped | 512 MB | no | rootfs storage only [estimate, cents per month] |
| mcp (Mcp phase) | stopped | 512 MB | no | rootfs storage only |
| import | does not exist between runs | n/a | no | nothing |
| authservice (Phase E) | running | 512 MB | yes (`min_machines_running = 1`) | full machine price |

So at launch the estate idles on **two running machines: postgres and api**. Phase E adds a third
(authservice). Everything else is stopped and costs almost nothing.

## 2. Which services pin a machine, and which synchronous call forces it?

Rule (Fly §7): for every in-request call A to B, either B keeps a machine running, or A's timeout
comfortably exceeds B's cold start. The second option is written down far more often than it is
configured, so this estate prefers the first.

| Pinned | Synchronous call that forces it | Why the alternative does not work |
|---|---|---|
| **postgres** | api and import open database connections in-request. There is no proxy to wake Postgres | A stopped database returns connection errors, and postgres has no auto-start path. It stays up by design |
| **api** | web's server-side `/api/proxy/[...path]` calls it for every search, and mcp calls it for every tool | Every search on the site would stall on a cold start. An agent tool call would time out. The api therefore stays pinned |
| **authservice** (E) | every validator fetches `/.well-known/jwks.json` in-request, and again when its cache expires (Fly §7) | Without a warm machine, a token check on the api or reviews adds a cold start to a request path that has no slack |
| **mcp** (not pinned at launch) | nothing calls mcp in-request. Agents are the only callers, and they are not in production yet | Pinning it now would pay for a machine no one uses. Section 3 names the trigger that changes this |
| **web** (not pinned) | entered from a browser only (Fly §7) | A cold start is a slow first page, not a failed call |

Consequence: the api is on the path of **both** web and mcp. Its single pinned machine is shared, so
scaling the api out later is a cost decision, not a correctness one. See section 7 for the migration
constraint on scaling.

## 3. The MCP cold-start budget, and the trigger to pin it

### 3.1 Budget (estimates, to be measured)

| Stage | Budget | Basis |
|---|---|---|
| Fly proxy starts a stopped machine | 1 to 3 s **[estimate]** | Fly's documented auto-start behaviour; not measured here |
| Container start, .NET runtime, kernel init, Kestrel listening | 2 to 4 s **[estimate]** | Typical .NET 10 cold start on shared-cpu-1x; not measured here |
| MCP `initialize` plus first tool call through the api (api already warm) | 0.1 to 0.3 s **[estimate]** | api is pinned, so no second cold start is added |
| **Total, first request after idle** | **target p95 at or below 8 s** | This is our chosen budget. It is a target, not a measurement |

Client side: an MCP client must allow longer than the budget for its first request. We assume common
agent clients allow 30 s or more **[assumption]**. The first real client must confirm this before the
budget is accepted. If a client allows less than 10 s, this decision must be reopened.

`grace_period = "60s"` in `mcp.fly.toml` covers the health check only. It is not the user-facing budget.

### 3.2 Named trigger to pin (`min_machines_running = 1`)

Trigger: **first real agent traffic**, as named in `docs/MASTER-PROMPT.md`. Concretely, any one of:

1. A non-test MCP client, an external agent, or a Claude integration is pointed at the production mcp URL.
   Pinning happens in the same change that onboards that client.
2. A client reports a timeout or a failed first request against the mcp endpoint.
3. Measured cold-start p95 is above 8 s over any 7-day window with real traffic.

When the trigger fires: set `min_machines_running = 1` in `flyio/mcp.fly.toml`, update the comment that
names the call (mcp to api, in-request), and record the change in this file with the date. Pinning mcp adds
one running machine (about 512 MB) to the idle bill in section 1. It is not automatic.

## 4. Cheaper options, and what each actually costs

Each row is a real decision someone could take. The cost column is an estimate.

| Option | Saves **[estimate]** | Cost of taking it | Decision |
|---|---|---|---|
| api `min_machines_running = 0` | ~3 to 6 EUR or USD per month | First search after idle gains a cold start of about 3 to 6 s. web's proxy timeout must cover it, and an in-request call that waits on a cold start is exactly what Fly §7 warns against | **Rejected.** The api is pinned. Revisit only if the site has no traffic for long periods |
| web `min_machines_running = 1` | (cost, not saving) ~3 to 6 per month | Buys nothing for a browser-only entry point | **Rejected** |
| Postgres `auto_stop` when idle | ~6 to 9 per month | The api cannot wake postgres. Every request during the stop returns an error | **Off the table** (section 7) |
| Postgres `shared-cpu-1x` at 512 MB, not 1 GB | ~2 to 3 per month | Less headroom for the catchment and ranking queries of Phase B+. Not measured | **Deferred.** Start at 1 GB, measure in Phase B, then decide |
| Scale api to 2 machines for resilience | (cost, not saving) ~3 to 6 per month | Two machines run `MigrateAsync` on start (section 7). Without a migration lock, schema work races | **Deferred** until a migration lock exists |
| Use mcp `min_machines_running = 0` at launch | ~3 to 6 per month | Cold start on the first agent call (section 3) | **Accepted** at launch, because nothing calls it yet. Reversed by the trigger in section 3.2 |

## 5. The one-shot import machine

Fly §5 and P12: a process that runs and exits cannot be a deployed app, because health checks would
report it dead. The import runs as a machine, not as an app.

### 5.1 Why it runs as a machine of the api app

- It reuses the api's secret store, so the connection string (`ConnectionStrings__catalogdb`) is not
  copied into a second app.
- It shares the api's region and 6PN network, so it reaches `school-search-postgres.internal:5432`
  directly. Postgres is always running, so `.internal` is the correct address (Fly §6).
- It gives one place to look for "what touched the catalog": one app.

### 5.2 Command pattern

The import image is built once in CI (Phase B), pushed to GHCR, and started by CI. Flags are written from
the Fly guide. **They are not executed here and must be checked against the installed flyctl version.**

```bash
flyctl machine run ghcr.io/konradcinkusz/school-search-import:<tag> \
  --app school-search-api-dev \
  --region waw \
  --restart no \
  --vm-size shared-cpu-1x --vm-memory 1024 \
  --name "import-<tag>" \
  --rm
```

- `--restart no`: without it Fly restarts the "crashed" job forever (Fly §5). A clean exit must be a success.
- `--rm` (or an explicit destroy after exit): destroys the machine on exit so the next run starts from a
  clean state. Verify the flag exists in the flyctl version CI pins.
- The job itself flushes its logs and sleeps about 2 s before exiting, so the log forwarder keeps the tail
  (Fly §5).
- CI waits for the machine to finish and reads the exit code (`flyctl machine status` or the wait
  subcommand). A non-zero exit fails the job. The exact wait command is to be verified in Phase B.

Idempotence: the import must be re-runnable (MASTER-PROMPT, Phase B gate). A re-run against the same
input must produce the same row counts, not duplicates.

### 5.3 Data handling

Olympiad pupil names are transient, in memory only. They are never persisted, logged or fixtured
(MASTER-PROMPT). The import machine has no volume, so there is nowhere on disk for them to land. This
is a property of the design, not a convention.

## 6. Costs (monthly, estimates)

All figures are **[estimate]**. They use approximate public list prices for Fly shared-cpu machines and
volumes, applied to region `waw`. Verify against the current Fly pricing page before any budget decision.

### 6.1 Assumptions (all estimates)

- Machine list price, approximately: shared-cpu-1x 512 MB about 3.2; shared-cpu-1x 1 GB about 5.7;
  256 MB about 1.9 (USD per month, always-on, 730 hours).
- Volume: about 0.15 per GB per month.
- Egress: about 0.02 per GB, from the region's price tier. `waw` may differ.
- Traffic at launch: under 100,000 requests per month, low single-digit GB egress.
- Import: one run per dataset release, about 10 to 60 minutes on a 1 GB machine.
- No reserved capacity, no paid support plan, no other Fly products.
- Figures are in USD (approximate) and are about the same magnitude in EUR.

### 6.2 Ranges

| Line item | Launch (Phases A to D) | With Phase E | Notes |
|---|---|---|---|
| postgres: 1 GB machine plus 10 GB volume | 6 to 9 | 6 to 9 | Lower bound uses the smaller memory size if measurements allow it |
| api: 1 pinned, 512 MB | 3 to 6 | 3 to 6 | Upper bound allows a second machine for a deploy overlap |
| web: scale to zero | 0 to 3 | 0 to 3 | Scales with browser traffic |
| mcp: scale to zero (not yet deployed) | 0 to 1 | 0 to 1 | Almost nothing until agent traffic exists |
| import: per run | 0.05 to 0.5 per run | 0.05 to 0.5 per run | Only on a dataset release |
| authservice: 1 pinned, 512 MB | not deployed | 3 to 6 | Phase E only |
| Egress, GHCR storage, misc. | 0 to 2 | 0 to 2 | Launch traffic |
| **Total per month** | **about 10 to 22** | **about 13 to 28** | Sum of the rows above, rounded |

Fixed monthly floor (postgres plus api): about 9 to 15. That is what the estate costs when it is quiet.

## 7. What is off the table

Stated so nobody re-proposes them without a recorded decision.

- **`force_https = false`** on any app. All public traffic is HTTPS through the proxy.
- **A public listener or IP on postgres.** No `[http_service]`, no `[[services]]`, no public IP ever.
  Use `fly proxy 15432:5432 --app school-search-postgres` from a laptop instead.
- **One database shared between services.** Physical co-location in one postgres instance is allowed, as
  a cost decision (P3). The logical boundary is not crossed. Phase E's authservice gets its own database
  inside the same instance (name to be set with its schema). It does not read `catalogdb`, and `catalogdb`
  is not given credentials for it.
- **Scaling postgres past one machine, or leaving `--ha` on.** A second machine would be a second empty
  volume (Fly §8). Postgres is never added to any scale workflow.
- **Scaling the api past one machine before a migration lock exists.** `MigrateAsync` runs as a hosted
  service on each machine start (P4). Two machines starting at once can race to apply the same
  migration. Until a lock is in the code, the api stays at one machine.
- **`.internal` for service-to-service HTTP.** A caller using `.internal` does not wake a stopped callee,
  so the call fails instead of waking it. Service-to-service HTTP uses the public URL (Fly §6).
- **A secret in `[env]`** of any `fly.toml` (Fly §4, §9).
- **A managed `postgres-flex` image.** It expects `fly postgres create` bootstrapping, not a plain
  `flyctl deploy` (Fly §14). The vanilla `postgres:17-alpine` image is used instead.
- **A shared signing key** across services. Exactly one service (authservice, Phase E) holds one. Every
  other service validates against its JWKS (P5).

## 8. Open items (not verified, not decided here)

1. GHCR pull for private images: the mechanism for a Fly secret pair (`GHCR_USER` and `GHCR_TOKEN`) has
   not been verified. The public-package fallback is the simpler path if it fails.
2. Health path for web is `/healthz`. The web app (A5) must serve it. If it does not, the check fails
   on the first deploy.
3. Authservice port (8080 assumed), its Dockerfile path, and the signing key's name and format come from
   the external repo. Unverified until Phase E.
4. CORS: browsers talk only to the web origin (BFF), so the api needs no browser-facing origin. If the
   kernel's `AddCorsPolicy` requires an origin configuration key, it must be set without guessing its name.
5. Import's flags (`--rm`, the wait command) to be verified against the pinned flyctl in Phase B.
6. Postgres collation (`--locale=C` is a placeholder; Polish ICU collation is a Phase B decision).
7. All cold-start figures in section 3.1 and all costs in section 6 are estimates, to be replaced with
   measured values after the first real deploy.

## 9. Data safety of the single Postgres machine (2026-10-14)

What the repository does today, stated without embellishment:

- One Postgres machine (`postgres:17-alpine`, 1 GB) with one 10 GB Fly volume. `--ha=false`. No replica, no standby, no failover.
- `flyio.yml` takes a volume snapshot (best effort, a failure only warns) before each Postgres redeploy.
- Fly takes platform snapshots of volumes on its own schedule with its own retention. **This repository neither configures
  nor verifies that schedule.** The owner must read the current Fly documentation and `flyctl volumes snapshots list` before
  relying on it.

What is missing:

- No logical backup (`pg_dump`) and no off-Fly copy. A volume snapshot lives in the same provider and region as the volume.
- No WAL archiving, so no point-in-time recovery: a restore returns to the snapshot instant.
- No restore drill. A backup that was never restored is not known to work.
- No managed failover: a host failure of the one machine means downtime until Fly recovers it or the volume is restored from
  a snapshot onto a new machine.
- The four databases share one machine and one role (ADR-0020): a disk-full or a runaway query in one is felt by all.

Any of these may be accepted for a launch with data that can be re-imported (the catalog) but not for data that cannot
(reviews, billing, accounts). The accepted-risk decision belongs to the owner and is recorded in ADR-0020.
