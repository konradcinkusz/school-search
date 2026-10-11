namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// Dictionary entry for an idTypOrgProw code. Only the code is linked to schools.
/// Labels are unverified until LabelVerified is true. IsNaturalPerson marks codes whose
/// governing body is a private individual (personal data: no name is ever stored).
/// </summary>
public sealed class GoverningBodyType
{
    public int Code { get; set; }

    public string? Label { get; set; }

    public bool LabelVerified { get; set; }

    public bool IsNaturalPerson { get; set; }

    public Provenance Provenance { get; set; } = new();
}
