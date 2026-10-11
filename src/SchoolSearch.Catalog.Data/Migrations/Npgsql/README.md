# Npgsql (PostgreSQL) migrations

Provider-specific migrations for `CatalogDbContext`. The Api applies them with `MigrateAsync`
from a hosted service after Kestrel starts (P4). `EnsureCreated` is used only on the InMemory path.

Status (Phase A):

- `20261011000000_InitialCreate.cs` is hand-written to mirror `CatalogDbContext.OnModelCreating`.
  It has not been compiled or applied against a real database.
- The Designer file (`*.Designer.cs`) and `CatalogDbContextModelSnapshot.cs` are **missing**.
  Generate them before the next schema change, on a machine with the .NET 10 SDK:

  ```
  dotnet ef migrations add <Name> --project src/SchoolSearch.Catalog.Data --startup-project src/SchoolSearch.Api --output-dir Migrations/Npgsql
  ```

  Check that the first generated diff is empty for `Schools`, or regenerate `InitialCreate` instead.
  Until then, `dotnet ef` would see no snapshot and propose a full CreateTable again.
- A SQL Server migrations folder is not created. Phase A targets PostgreSQL only.
