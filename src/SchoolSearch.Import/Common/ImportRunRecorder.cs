using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Import;

/// <summary>
/// Writes one <see cref="ImportRun"/> row per command. The row is inserted before the loader runs and
/// completed (FinishedAt and counts) only after it returns. A null FinishedAt means the run did not
/// complete: the loader threw, or the process was stopped. Run timestamps use the wall clock, not the
/// as-of clock given to the loader.
/// </summary>
public sealed class ImportRunRecorder(CatalogDbContext db, TimeProvider wallClock)
{
    public async Task<ImportRun> StartAsync(string kind, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var run = new ImportRun
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            StartedAt = wallClock.GetUtcNow(),
            FinishedAt = null,
            RowsRead = 0,
            RowsWritten = 0,
            RowsRejected = 0,
            SchemaVersion = CatalogSchema.LatestMigrationId,
        };

        db.ImportRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    public async Task CompleteAsync(ImportRun run, ImportReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(report);

        run.FinishedAt = wallClock.GetUtcNow();
        run.RowsRead = report.RowsRead;
        run.RowsWritten = report.RowsWritten;
        run.RowsRejected = report.RowsRejected;

        await db.SaveChangesAsync(ct);
    }
}
