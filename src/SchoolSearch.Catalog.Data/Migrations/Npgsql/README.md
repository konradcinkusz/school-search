# Npgsql (PostgreSQL) migrations

Provider-specific migrations for `CatalogDbContext`. The Api applies them with `MigrateAsync`
from a hosted service after Kestrel starts (P4). `EnsureCreated` is used only on the InMemory path.

Migrations, in order:

| Id | Class | Content |
| --- | --- | --- |
| `20261011000000_InitialCreate` | `InitialCreate` | Phase A: `Schools` (SIO register subset). |
| `20261011010000_Phase_B_Ingest` | `PhaseBIngest` | Phase B: new `Schools` columns (`IsSpecial`, `FaithAffiliationDerived`, `FaithAffiliationDeclared`, `Latitude`, `Longitude`, owned `CoordinatesProvenance_*`); tables `ClassCohorts`, `ExamResults`, `NationalBaselines`, `GoverningBodyTypes`, `ImportRuns`. |

The newest id is `CatalogSchema.LatestMigrationId` (`src/SchoolSearch.Catalog.Data/CatalogSchema.cs`).
Update it with every new migration. The importer refuses to run (exit 2) when `__EFMigrationsHistory`
lacks that id.

Status:

- Both migration files are **hand-written** to mirror `CatalogDbContext.OnModelCreating`. Neither has
  been compiled or applied against a real database.
- Phase B reuses the Phase A column `Schools.TypeOfGoverningBody` for `School.GoverningBodyTypeCode`
  (`HasColumnName`), so no rename is needed.
- Composite keys stand in for the natural-key unique constraints of `ClassCohorts`, `ExamResults`
  and `NationalBaselines`; the primary key is the unique key.
- No foreign keys are declared (the entities carry plain `SchoolId` values). The loaders enforce
  that a school exists before writing its rows.
- The Designer files (`*.Designer.cs`) and `CatalogDbContextModelSnapshot.cs` are **missing**.
  Without a snapshot, EF 9+ reports `PendingModelChangesWarning` on `MigrateAsync`. The context
  suppresses that warning in `CatalogDbContext.OnConfiguring`. Remove the suppression once the snapshot exists.
  Generate the snapshot on a machine with the .NET 10 SDK:

  ```
  dotnet ef migrations add <Name> --project src/SchoolSearch.Catalog.Data --startup-project src/SchoolSearch.Api --output-dir Migrations/Npgsql
  ```

  Check that the first generated diff is empty for both migrations above. If it is not, regenerate
  the two migrations rather than adding a third. Until then, `dotnet ef` would see no snapshot and
  propose a full CreateTable again.
- A SQL Server migrations folder is not created. Phase A and B target PostgreSQL only.
