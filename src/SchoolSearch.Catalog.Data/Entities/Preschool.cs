namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// A preschool (przedszkole) from the SIO register (CC0 dataset 288), with its location.
/// A preschool never gets a quality score, rank, grade or any ordinal judgement: no such member
/// exists here or in any Preschool contract (ADR-0014; enforced by PreschoolNoScoreTests).
/// </summary>
/// <remarks>
/// The governing body is stored as its type code only, as for schools. Its name is never stored.
/// Practical facts (tuition, hours, free places) are optional and live in <see cref="PreschoolFacts"/>.
/// </remarks>
public sealed class Preschool
{
    /// <summary>SIO idPodmiot. Primary key.</summary>
    public long IdPodmiot { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>TERYT code of the gmina (7 characters), when known.</summary>
    public string? GminaTeryt { get; set; }

    /// <summary>Public name of the gmina, when known (max 120).</summary>
    public string? GminaName { get; set; }

    /// <summary>idTypOrgProw code of the governing body. Code only, never a name.</summary>
    public int? GoverningBodyTypeCode { get; set; }

    /// <summary>Public and non-public preschools are both listed. Defaults to false until reviewed.</summary>
    public bool IsPublic { get; set; }

    /// <summary>Public address of the unit (max 200).</summary>
    public string? Street { get; set; }

    /// <summary>Postal code of the unit (max 10).</summary>
    public string? PostalCode { get; set; }

    /// <summary>City of the unit (max 120).</summary>
    public string? City { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public Provenance Provenance { get; set; } = new();

    /// <summary>Practical facts. Null when no facts row exists (the UI shows "brak danych").</summary>
    public PreschoolFacts? Facts { get; set; }
}
