namespace SchoolSearch.Contracts.Public;

/// <summary>
/// One preschool in a list. Practical facts only: a preschool has no quality score, rank or grade (ADR-0014).
/// </summary>
public sealed record PreschoolListItem(
    string Slug,
    string Name,
    string? GminaTeryt,
    string? GminaName,
    bool IsPublic,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    MoneyRange MonthlyTuition,
    int? FreePlacesCount);
