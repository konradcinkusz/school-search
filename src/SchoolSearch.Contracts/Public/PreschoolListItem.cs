namespace SchoolSearch.Contracts.Public;

/// <summary>
/// One preschool in a list. Practical facts only: a preschool has no quality score, rank or grade (ADR-0014).
/// <see cref="MonthlyTuition"/> and <see cref="FreePlacesCount"/> are the values alone; <see cref="Facts"/> is the same
/// block the profile carries (tuition, meal, hours, free places) with its provenance, so a list card can show the
/// source and date of each fact without a profile request. <see cref="Facts"/> is null when no facts row exists.
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
    int? FreePlacesCount,
    PreschoolFactsBlock? Facts);
