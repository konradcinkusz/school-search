namespace SchoolSearch.Catalog.Data;

/// <summary>Schema facts shared by the Api and the importer.</summary>
public static class CatalogSchema
{
    /// <summary>
    /// Id of the newest migration. The importer refuses to run (exit 2) when
    /// __EFMigrationsHistory does not contain it. Update together with the newest migration.
    /// </summary>
    public const string LatestMigrationId = "20261011010000_Phase_B_Ingest";
}
