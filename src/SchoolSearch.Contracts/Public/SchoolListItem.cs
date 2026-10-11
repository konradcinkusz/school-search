namespace SchoolSearch.Contracts.Public;

/// <summary>
/// One school in a list. <see cref="ScoreStatus"/> is "published", "insufficient_cohort", "no_exam_data" or
/// "no_baseline". <see cref="Score"/> is null for the last two (no number exists to show).
/// </summary>
public sealed record SchoolListItem(
    string Slug,
    string Name,
    string? GminaTeryt,
    string? GminaName,
    bool IsPublic,
    bool IsSpecial,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    string ScoreStatus,
    ScoreBlock? Score);
