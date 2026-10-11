namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>Audit record of one importer run. Idempotent upserts mean counts describe this run only.</summary>
public sealed class ImportRun
{
    public Guid Id { get; set; }

    /// <summary>Loader kind, for example "sio", "cke", "baseline", "geo".</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public int RowsRead { get; set; }

    public int RowsWritten { get; set; }

    public int RowsRejected { get; set; }

    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>Provenance of the input file the run read.</summary>
    public Provenance Provenance { get; set; } = new();
}
