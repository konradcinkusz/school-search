# Architecture: school-search

This repository is measured against the reference architecture (the constitution), and this file records
where it deliberately differs. It does not restate the constitution.

- Constitution: [`docs/architecture/00-REFERENCE-ARCHITECTURE.md`](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/architecture/00-REFERENCE-ARCHITECTURE.md) in `konradcinkusz/architecture-standards`.
- Scaffold procedure: `docs/scaffold/INIT-GENERIC-TEMPLATE.md` in the same repository.
- Decisions: [`docs/adr/`](../adr/).

## Shape today (Phase A)

| Concern | Here | Constitution |
|---|---|---|
| Composition root | `src/SchoolSearch.AppHost`, development only | P1 |
| Shared kernel | `src/SchoolSearch.ServiceDefaults`, plumbing only, size-checked | P2 |
| Services | `src/SchoolSearch.Api` (catalog), owns `catalogdb` | P3 |
| Persistence | provider switch `DATABASE_PROVIDER`, InMemory fallback, `MigrateAsync` in a hosted service | P4 |
| Secrets | environment and platform secrets, scanner in hook and CI | P5 |
| Signing keys | none in this repository (no user accounts before Phase E, ADR-0004) | P5 |
| Container | multi-stage Dockerfiles, `:8080`, runtime major = TFM major | P6 |
| Platform | Fly.io, one app per service, `waw` (ADR-0002) | P7 |
| Degradation | `/health` lists every optional integration; banner prints the same list | P8 |
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
| 2026-10-11 | Interim `ADMIN_API_KEY`: a single static secret protects admin endpoints until identity exists. | P5 (identity through the platform; no shared secret that grants a role); IDENTITY-AND-ACCOUNTS guide | No user accounts before Phase E. The admin surface cannot wait for authservice. | Open. Exit at Phase E: admin role via authservice tokens, then remove `ADMIN_API_KEY`. | [ADR-0008](../adr/0008-interim-admin-api-key.md) |

If a row has no exit condition, it is not a deviation register entry; it is drift.

## Known gaps in this document

- The MCP service is not in this tree. Its architecture is recorded in ADR-0006 and will be measured here when it exists.
- The Phase B scoring engine and import job are in review. They are not compiled in the build environment that wrote
  the Phase B docs. Their shape is recorded in ADR-0007, ADR-0009, ADR-0012 and ADR-0013.
- The constitution's P8 zero-credential run and the container health gates are not claimed as passed here. See the verification record in the delivery pull request.
