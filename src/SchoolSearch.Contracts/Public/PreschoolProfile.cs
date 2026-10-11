namespace SchoolSearch.Contracts.Public;

/// <summary>
/// Full public view of one preschool (<c>GET /api/v1/preschools/{slug}</c>). It carries no score of any kind.
/// <see cref="Facts"/> is null when no facts row exists; the client shows "brak danych".
/// </summary>
public sealed record PreschoolProfile(
    string Slug,
    string Name,
    string? GminaTeryt,
    string? GminaName,
    bool IsPublic,
    string? Street,
    string? PostalCode,
    string? City,
    double? Latitude,
    double? Longitude,
    GoverningBodyInfo GoverningBody,
    PreschoolFactsBlock? Facts,
    string Notice);
