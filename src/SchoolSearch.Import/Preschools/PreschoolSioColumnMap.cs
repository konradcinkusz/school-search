namespace SchoolSearch.Import.Preschools;

/// <summary>
/// Column and value names of the SIO preschool units (CSV export of dane.gov.pl dataset 288) as the loader reads them.
/// </summary>
/// <remarks>
/// Every default is UNVERIFIED: it is not checked against the published file (the publisher host is unreachable
/// from the build environment, see docs/data/SOURCES.md). A mapped header missing from the file aborts the run
/// before any write. Optional columns are set to null to switch them off in a JSON override; a non-null optional
/// column that is missing from the file also aborts the run.
/// The organ-prowadzacy NAME column is intentionally not part of this map: it is never read.
/// </remarks>
public sealed record PreschoolSioColumnMap
{
    // UNVERIFIED header names.
    public string IdPodmiot { get; init; } = "idPodmiot";

    public string TypPodmiotu { get; init; } = "TypPodmiotu";

    public string Name { get; init; } = "Nazwa";

    public string TerytGmina { get; init; } = "TerytGmina";

    /// <summary>Optional. Public name of the gmina.</summary>
    public string? GminaName { get; init; } = "Gmina";

    public string Publicznosc { get; init; } = "Publiczność";

    /// <summary>idTypOrgProw. Stored as the governing body type code only.</summary>
    public string OrganProwTypeCode { get; init; } = "idTypOrgProw";

    /// <summary>Optional. Public address of the unit.</summary>
    public string? Street { get; init; } = "Ulica";

    /// <summary>Optional. Public address of the unit.</summary>
    public string? PostalCode { get; init; } = "KodPocztowy";

    /// <summary>Optional. Public address of the unit.</summary>
    public string? City { get; init; } = "Miejscowosc";

    // UNVERIFIED value names. Compared case-insensitively after trimming.

    /// <summary>Value of TypPodmiotu that selects preschool units.</summary>
    public string PreschoolType { get; init; } = "Przedszkole";

    /// <summary>Value of Publicznosc that sets IsPublic to true.</summary>
    public string PublicValue { get; init; } = "publiczna";

    /// <summary>Throws <see cref="ArgumentException"/> when a required name is blank or an optional name is blank.</summary>
    public void Validate()
    {
        var required = new[]
        {
            nameof(IdPodmiot), IdPodmiot,
            nameof(TypPodmiotu), TypPodmiotu,
            nameof(Name), Name,
            nameof(TerytGmina), TerytGmina,
            nameof(Publicznosc), Publicznosc,
            nameof(OrganProwTypeCode), OrganProwTypeCode,
            nameof(PreschoolType), PreschoolType,
            nameof(PublicValue), PublicValue,
        };

        for (var i = 0; i < required.Length; i += 2)
        {
            if (string.IsNullOrWhiteSpace(required[i + 1]))
            {
                throw new ArgumentException($"PreschoolSioColumnMap.{required[i]} must not be empty.");
            }
        }

        var optional = new[]
        {
            nameof(GminaName), GminaName,
            nameof(Street), Street,
            nameof(PostalCode), PostalCode,
            nameof(City), City,
        };

        for (var i = 0; i < optional.Length; i += 2)
        {
            if (optional[i + 1] is not null && string.IsNullOrWhiteSpace(optional[i + 1]))
            {
                throw new ArgumentException($"PreschoolSioColumnMap.{optional[i]} must be null or not empty.");
            }
        }
    }
}
