namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// A school from the SIO register (CC0 dataset 288). Only the fields the Phase A
/// surface needs. There is deliberately no teacher, pupil or nationality entity
/// (anti-goals, enforced by absence).
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
    public int? TypeOfGoverningBody { get; set; }

    /// <summary>Only public schools are served. Defaults to false until reviewed.</summary>
    public bool IsPublic { get; set; }

    public Provenance Provenance { get; set; } = new();
}
