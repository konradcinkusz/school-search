namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// A school from the SIO register (CC0 dataset 288). There is deliberately no teacher, pupil
/// or nationality entity (anti-goals, enforced by absence).
/// </summary>
/// <remarks>
/// The governing body is stored as its type code only (idTypOrgProw). The governing
/// body's name is never stored: for private individuals (codes 141/146/150) it is
/// personal data. The dictionary behind the code is still unverified (docs/data/SOURCES.md).
/// </remarks>
public sealed class School
{
    /// <summary>SIO idPodmiot. Stability across years is still unverified (SOURCES).</summary>
    public long IdPodmiot { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    /// <summary>TERYT code of the gmina (7 characters), when known.</summary>
    public string? GminaTeryt { get; set; }

    /// <summary>idTypOrgProw code of the governing body. Code only, never a name.</summary>
    public int? GoverningBodyTypeCode { get; set; }

    /// <summary>
    /// Phase A name, kept so existing code and tests compile. Same column as
    /// <see cref="GoverningBodyTypeCode"/>; not mapped to its own column.
    /// </summary>
    public int? TypeOfGoverningBody
    {
        get => GoverningBodyTypeCode;
        set => GoverningBodyTypeCode = value;
    }

    /// <summary>Only public schools are served. Defaults to false until reviewed.</summary>
    public bool IsPublic { get; set; }

    /// <summary>Special-education school (SpecyfikaSzkoly). Defaults to false.</summary>
    public bool IsSpecial { get; set; }

    /// <summary>
    /// Derived flag: true only when the governing body type is 140 (kościelna osoba prawna).
    /// Never derived from the school name or patron.
    /// </summary>
    public bool FaithAffiliationDerived { get; set; }

    /// <summary>Faith affiliation as declared in a source, when one exists. Null means not declared.</summary>
    public bool? FaithAffiliationDeclared { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    /// <summary>Provenance of the coordinates. Null until a geo file has been loaded.</summary>
    public Provenance? CoordinatesProvenance { get; set; }

    public Provenance Provenance { get; set; } = new();
}
