# Architecture: school-search

This repository is measured against the reference architecture (the constitution), and this file records
where it deliberately differs. It does not restate the constitution.

- Constitution: [`docs/architecture/00-REFERENCE-ARCHITECTURE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md) in `konradcinkusz/architecture-standards`.
- Scaffold procedure: `docs/scaffold/INIT-GENERIC-TEMPLATE.md` in the same repository.
- Decisions: [`docs/adr/`](../adr/).

## Shape today (through Phase E, in review)

| Concern | Here | Constitution |
|---|---|---|
| Composition root | `src/SchoolSearch.AppHost`, development only | P1 |
| Shared kernel | `src/SchoolSearch.ServiceDefaults`, plumbing only, size-checked | P2 |
| Services | `src/SchoolSearch.Api` (catalog), owns `catalogdb`; `src/SchoolSearch.Reviews` (reviews, DSA notices, moderation), owns `reviewsdb` (Phase E) | P3 |
| Persistence | provider switch `DATABASE_PROVIDER`, InMemory fallback, `MigrateAsync` in a hosted service | P4 |
| Secrets | environment and platform secrets, scanner in hook and CI | P5 |
| Signing keys | one key, held by authservice (external identity provider, `konradcinkusz/authservice`). This repository holds no signing key and mints no token; it validates RS256 tokens against authservice's JWKS (ADR-0004, ADR-0015) | P5 |
| Identity | authservice is the only identity provider. Accounts, passwords, 2FA and token issuance live there. The web BFF holds the refresh token server-side (Phase E) | P5, FRONTEND-BFF guide |
| Container | multi-stage Dockerfiles, `:8080`, runtime major = TFM major | P6 |
| Platform | Fly.io, one app per service, `waw` (ADR-0002) | P7 |
| Degradation | `/health` lists every optional integration; banner prints the same list. Auth-gated endpoints answer 503 `auth_unavailable` without `Auth:Authority` | P8 |
| Frontend | Next.js standalone, server-side proxy, runtime `/api/config` | FRONTEND-BFF guide |
| Tests | xUnit (InMemory), architecture tests, Playwright e2e wired to CI | P13 |
| CI/CD | tag-driven deploy; missing Fly app always selected; build once | P12 |
| Metric rules | anti-goals enforced by absence, counter-metric blended, confidence beside score | METRIC-ETHICS guide |

## Deviation register

Each row is dated and carries a reason. A deviation is removed when it is fixed. An accepted deviation
stays here with its ADR. Rows are not deleted to hide history.

| Date | Deviation | Principle or guide breached | Reason | Status / exit condition | Record |
|---|---|---|---|---|---|
| 2026-10-11 | Dependency version-update automation declared in `.github/dependabot.yml` with `open-pull-requests-limit: 0` for every ecosystem, so it opens no version-update PRs. Dependabot security alerts and security updates are a repository setting, not this file (state recorded in ADR-0005). | REPO-BASELINE §1 (dependency-update automation is a baseline item) | Nobody owns triage yet. Automation without triage is worse than none. Vulnerability detection is covered by `NuGetAuditMode=all` and the CodeQL workflow. | Open. Exit when a maintainer triages dependency PRs, or the repository goes public. Then raise the limit per ecosystem. | [ADR-0005](../adr/0005-dependabot-declared-off.md) |
| 2026-10-11 | `SchoolSearch.Import` (planned, Phase B) and `SchoolSearch.Api` share the `SchoolSearch.Catalog.Data` project, so both deployables compile the same `CatalogDbContext` and entities. | P3 (one service owns its database; no second service opens it); §4 non-goal "no shared ORM entities across services" | One bounded context (Catalog) with one database. The import job is a second deployable of the same context, not a second context. | Open, accepted. Exit if Import becomes a separate bounded context with its own schema, then split `Catalog.Data`. | [ADR-0007](../adr/0007-catalog-two-deployables.md) |
| 2026-10-11 | Phase B migrations are hand-written for Npgsql (no model snapshot can be generated in the Phase B build environment), and `PendingModelChangesWarning` is suppressed for `CatalogDbContext`, so EF does not report drift between model and migration at startup. The import writes `catalogdb` directly (ADR-0007). | P4 (persistence: the migrations record the model) | No .NET SDK in the Phase B build environment, so a snapshot cannot be generated there. The hand-written migration cannot be proven against the model by the compiler. Compensating controls: review against the entity configuration, the `check-schema` guard (exit 2 on a missing migration), InMemory tests. | Open. Exit: generate the snapshot on a machine with the .NET 10 SDK, confirm the generated SQL matches the hand-written migration, remove the warning suppression. | [ADR-0013](../adr/0013-import-direct-catalogdb-writes.md) |
| 2026-10-11 | The AI explanation client (`src/SchoolSearch.Api/Ai`) runs in-process in the catalog API, not as a separate service. It holds `ANTHROPIC_API_KEY`. | P10 (a credential-bearing dependency with an availability risk becomes a service) | One AI feature, one endpoint, and no batch job needs the key yet. The master brief rules out a second service without a recorded reason. | Open, accepted. Exit: a batch job (AI-FEATURES features 3 or 6) needs the key outside the API, or a second AI feature needs its own availability, then move the client to a service. | [ADR-0011](../adr/0011-anthropic-api-usage-boundaries.md) (decision 2) |
| 2026-10-11 | The AI daily token budget and response cache are in memory, per process. The proposal named Postgres tables (`ai_usage`, `ai_cache`). | Cost control in `docs/ai/AI-FEATURES.md` §3.3 and §7 (not a constitution principle) | The API runs as one instance today. A Postgres table would add a write on every explanation for little gain at this size. The counter resets on restart, so the limit is a soft cap. | Open. Exit: before a second API instance runs, or before any feature beyond 1 ships, move the counter to Postgres. | [ADR-0011](../adr/0011-anthropic-api-usage-boundaries.md) (decisions 10 and 11) |
| 2026-10-11 | Interim `ADMIN_API_KEY`: a single static secret protects admin endpoints until identity exists. | P5 (identity through the platform; no shared secret that grants a role); IDENTITY-AND-ACCOUNTS guide | No user accounts before Phase E. The admin surface cannot wait for authservice. | Closed in the Phase E change set, once E2 lands: admin access is an authservice token with role `Admin` or `SuperAdmin`, and `ADMIN_API_KEY` is removed from code, configuration and docs. Row kept for history. | [ADR-0008](../adr/0008-interim-admin-api-key.md) (superseded by [ADR-0015](../adr/0015-authservice-adoption-and-reviews-service.md)) |
| 2026-10-20 | The Reviews service validates authservice tokens with its own JWT setup code, a copy of the Api's `Auth/` extension. No shared authentication library. | P2 (the kernel stays small, size-checked in CI) and P3 (service independence), as applied here | The kernel stays under its line ceiling. A shared auth library would couple two deployables for about 80 lines. | Open, accepted. Exit: a third consumer, or a change in validation rules that must land in both at once. | [ADR-0015](../adr/0015-authservice-adoption-and-reviews-service.md) |
| 2026-10-20 | authservice and the Reviews service share one Postgres instance (`school-search-postgres`), with one database each (`catalogdb`, `reviewsdb`, `authservicedb`). | P3 (one service owns its database; a database per service on separate instances is the strict reading) | One instance keeps cost and operations at the current scale. Separate databases keep the schemas and credentials of each service apart. A single instance is also a single point of failure for all three. | Open, accepted. Exit: a second service with its own availability target, or a backup and restore requirement that the single volume cannot meet. | [ADR-0015](../adr/0015-authservice-adoption-and-reviews-service.md) |
| 2026-10-20 | The reviewer's identity is stored as `SHA-256(pepper + sub)`. Holders of the pepper can test a known subject against stored hashes. | Data minimisation (RODO art. 5(1)(c)) | The stored value must still support the one-review-per-author rule. A hash with a server-side pepper is the least data that supports it. | Open, accepted. Exit: a review model that needs no author key (for example, a one-time token per review), or a pepper rotation plan with migration. | [ADR-0015](../adr/0015-authservice-adoption-and-reviews-service.md), `docs/security/SECURITY-REVIEW-PHASE-E.md` T16 |
| 2026-10-20 | The DSA machinery and the Polish terms and privacy notice are drafts for legal review. The contact point is unset until the owner supplies it. | AGENTS.md hard rule: no invented legal basis | Legal texts need the service provider's identity and a lawyer. The code reports the gap (503 `contact_not_configured`) instead of guessing. | Open. Exit: legal approval recorded in `docs/legal/`, and `Dsa__ContactEmail` set in `flyio/reviews.fly.toml`. | [DSA mapping](../legal/DSA.md) |
| 2026-10-20 | The AppHost runs authservice as a container, with a fixed host port and a local RS256 key from user secrets. Opt-in only (`IncludeAuthservice`). | P1 (the AppHost is development only, not the production topology) | Local login testing needs the real identity provider. A fixed port keeps the public base URL known in advance. | Open, accepted. Exit: a local test double for authservice if the container image becomes a barrier to contributors. | [Runbook](../runbooks/AUTHSERVICE.md) section 5 |

If a row has no exit condition, it is not a deviation register entry; it is drift.

## Known gaps in this document

- The MCP service is in this tree (`src/SchoolSearch.Mcp`, PR 4) but is **not deployed**, not compiled in the authoring environment, and its protocol test has not run in CI. Its architecture is ADR-0006. It is not yet measured against the constitution's gates; that measurement belongs with its first deploy.
- The AI explanation endpoint (PR 4) is not compiled or run in CI in the authoring environment. Its boundaries are ADR-0011, and its payload allowlist is an open item (ADR-0011 O2).
- The Phase B scoring engine and import job are in review. They are not compiled in the build environment that wrote
  the Phase B docs. Their shape is recorded in ADR-0007, ADR-0009, ADR-0012 and ADR-0013.
- The constitution's P8 zero-credential run and the container health gates are not claimed as passed here. See the verification record in the delivery pull request.
- Phase E (accounts and reviews) is written without a .NET SDK or actionlint in the authoring environment. Its gates are NOT RUN until CI runs them. The security record is `docs/security/SECURITY-REVIEW-PHASE-E.md`.
