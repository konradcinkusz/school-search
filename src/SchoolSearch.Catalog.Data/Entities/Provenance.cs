namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// Where a published value came from. Stored as owned columns on the owning row.
/// Every published value carries source, sourceUrl, asOf and method (MASTER-PROMPT, Data).
/// </summary>
public sealed class Provenance
{
    public string Source { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public DateOnly AsOf { get; set; }

    public string Method { get; set; } = string.Empty;
}
