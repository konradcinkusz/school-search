using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Import;

/// <summary>
/// Refuses to write into a catalog whose schema is not the one the importer was built for (exit code 2).
/// The check is the presence of <see cref="CatalogSchema.LatestMigrationId"/> in __EFMigrationsHistory.
/// On the InMemory provider there are no migrations: the check is a no-op and returns true.
/// </summary>
public static class SchemaGuard
{
    public static async Task<bool> IsCompatibleAsync(CatalogDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (!db.Database.IsRelational())
        {
            return true;
        }

        // EF returns an empty list when the history table does not exist yet, which is a mismatch, not an error.
        var applied = await db.Database.GetAppliedMigrationsAsync(ct);
        return applied.Contains(CatalogSchema.LatestMigrationId, StringComparer.Ordinal);
    }
}
