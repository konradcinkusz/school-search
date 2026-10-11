namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// One street (or a number range on it) assigned to a school's catchment zone by a gmina's
/// resolution. StreetNormalized is lower-case with diacritics folded. Parity is "even", "odd" or
/// null (both). NumberFrom and NumberTo are null when the rule covers the whole street.
/// Index: (GminaTeryt, StreetNormalized). No foreign keys (loaders check the school exists).
/// </summary>
public sealed class CatchmentStreet
{
    /// <summary>Surrogate key, identity column.</summary>
    public long Id { get; set; }

    /// <summary>TERYT code of the gmina (7 characters).</summary>
    public string GminaTeryt { get; set; } = string.Empty;

    public long SchoolId { get; set; }

    /// <summary>Lower-case street name with diacritics folded. Max 200 characters.</summary>
    public string StreetNormalized { get; set; } = string.Empty;

    public int? NumberFrom { get; set; }

    public int? NumberTo { get; set; }

    /// <summary>"even", "odd", or null for both.</summary>
    public string? Parity { get; set; }

    public Provenance Provenance { get; set; } = new();
}
