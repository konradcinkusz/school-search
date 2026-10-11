# ADR-0013: Import writes catalogdb directly, with hand-written migrations until a snapshot can be generated

- **Status:** Accepted as an interim decision, with an exit condition below
- **Date:** 2026-10-11
- **Deciders:** owner, at Phase B review

## Context

Phase B adds new entities (`ClassCohort`, `ExamResult`, `NationalBaselineRow`, `GoverningBodyType`, `ImportRun`)
to `CatalogDbContext` (`src/SchoolSearch.Catalog.Data`). Entity Framework generates migrations from a model
snapshot, and the snapshot is produced by the `dotnet` tooling. The Phase B build environment has no .NET SDK, so
the snapshot cannot be generated there. The new migration under `Migrations/Npgsql` is therefore hand-written.

Separately, ADR-0007 has the import job write the catalog database directly, as a second deployable of the same
`Catalog.Data` context, rather than through the API. That decision stands here.

Without a snapshot that matches the model, EF raises `PendingModelChangesWarning` on startup and treats it as an
error under `TreatWarningsAsErrors`. The hand-written migration is correct only if it matches the model, and the
tooling cannot prove that here.

## Decision

1. **The import writes `catalogdb` directly**, through `CatalogDbContext`, as ADR-0007 records. It does not call
   the API.
2. **The Phase B migrations are hand-written** under `src/SchoolSearch.Catalog.Data/Migrations/Npgsql`, for the
   Postgres provider only.
3. **`PendingModelChangesWarning` is suppressed** for this context, so that the hand-written migration does not
   fail startup. The suppression is recorded in the deviation register (`docs/architecture/00-ARCHITECTURE.md`).
4. **A guard remains.** `CatalogSchema.LatestMigrationId` is checked against `__EFMigrationsHistory` by
   `check-schema`. The importer refuses to run (exit code 2) when the newest migration is not applied.

## Options considered

| Option | For | Against |
|---|---|---|
| **Hand-written migration, warning suppressed, direct write (chosen)** | Phase B can ship without the .NET SDK here; the schema change is reviewable as SQL-like code | The compiler cannot confirm the migration matches the model; a drift would not be reported at startup |
| Generate the snapshot on a machine with `dotnet` before merging | Correct by construction | Blocks Phase B on tooling not available to this agent; the snapshot would be generated late |
| Import through the API | One writer for the schema | Puts a long bulk job in the serving process (rejected in ADR-0007) |
| Keep the warning and fail startup | No risk of undetected drift | Startup fails until the snapshot exists, so nothing runs |

## Consequences

- **Risk accepted:** model drift from the migration is not reported by EF at startup. Compensating controls:
  the review of the hand-written migration against the entity configuration, the `check-schema` guard, and the
  InMemory tests, which exercise the model (the InMemory provider does not read the migration).
- The suppression must not spread: it applies to this context only, and it is written in one place (where the
  Api or the context registration applies it; the Phase B code review confirms the location).
- No raw SQL is placed in model configuration, so the InMemory tests keep working.

## Exit condition

Generate the model snapshot on a machine with the .NET 10 SDK, using the same entity configuration. Confirm that the
generated migration produces the same schema as the hand-written one (compare the SQL script for the new
migration). Commit the snapshot, remove the `PendingModelChangesWarning` suppression, and run `check-schema` and the
Postgres migration in CI. Then mark this ADR Superseded or Accepted in full, and close the deviation row.
