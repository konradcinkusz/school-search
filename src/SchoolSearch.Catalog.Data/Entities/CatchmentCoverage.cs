namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// Per-gmina state of catchment-zone data. State "none" means no data (the API must not
/// synthesise a boundary); "street_list" means a street list is published; "polygon" is reserved.
/// Natural key: GminaTeryt.
/// </summary>
public sealed class CatchmentCoverage
{
    /// <summary>TERYT code of the gmina (7 characters).</summary>
    public string GminaTeryt { get; set; } = string.Empty;

    /// <summary>One of "none", "street_list", "polygon".</summary>
    public string State { get; set; } = "none";

    /// <summary>Link to the gmina's resolution that defines the catchment zones, when known.</summary>
    public string? ResolutionUrl { get; set; }

    /// <summary>Date of that resolution, when known.</summary>
    public DateOnly? ResolutionDate { get; set; }

    public Provenance Provenance { get; set; } = new();
}
