namespace SchoolSearch.Contracts.Public;

/// <summary>
/// One point on the map. A marker carries location and practical facts only: no score, rank, review, sponsorship or
/// billing value is read or returned. <see cref="Kind"/> is "school" or "preschool". <see cref="MonthlyTuition"/> and
/// <see cref="FreePlacesCount"/> are set for preschools only (null for schools); a preschool without a facts row has
/// a <see cref="MoneyRange"/> with null bounds and a null <see cref="FreePlacesCount"/> ("brak danych").
/// </summary>
public sealed record MapMarker(
    string Kind,
    string Slug,
    string Name,
    double Latitude,
    double Longitude,
    bool IsPublic,
    string? GminaName,
    MoneyRange? MonthlyTuition,
    int? FreePlacesCount);

/// <summary>
/// Markers inside a bounding box. <see cref="Total"/> counts every match in the box after filters;
/// <see cref="Truncated"/> is true when <see cref="Total"/> exceeds <see cref="Limit"/>. Items are ordered by slug
/// (then kind), so a truncated subset is arbitrary, never "the best".
/// </summary>
public sealed record MapMarkersResponse(
    IReadOnlyList<MapMarker> Items,
    int Total,
    bool Truncated,
    int Limit);
