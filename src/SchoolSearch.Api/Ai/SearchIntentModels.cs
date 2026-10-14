namespace SchoolSearch.Api.Ai;

/// <summary>Body of <c>POST /api/v1/ai/search-intent</c>.</summary>
public sealed record SearchIntentRequest(string? Question);

/// <summary>
/// Filters the parent can see and edit. Only these fields can reach the search query; the caller applies them through
/// the normal search API, and the model never sees results. Null means "not set". <c>Kind</c> is school, preschool or
/// all; <c>MaxTuitionPln</c> is 0..20000; <c>Place</c> is a free-text place phrase and never coordinates.
/// </summary>
public sealed record SearchIntentFilters(
    string? Gmina,
    string? Q,
    bool? PublicOnly,
    double? MaxDistanceKm,
    string? Type,
    string? Kind,
    int? MaxTuitionPln,
    bool? HasFreePlaces,
    string? Place,
    IReadOnlyList<string> Notes);

/// <summary>The response of <c>POST /api/v1/ai/search-intent</c>.</summary>
public sealed record SearchIntentResponse(
    SearchIntentFilters Filters,
    string AiLabel,
    string Model,
    IReadOnlyList<string> Unsupported);

public enum SearchIntentStatus
{
    Ok,
    InvalidInput,
    Unavailable,
    BudgetExhausted,
    RateLimited,
    OutputRejected,
}

/// <summary>Result of one interpretation. <see cref="Response"/> is set only for Ok; <see cref="Message"/> only for InvalidInput.</summary>
public sealed record SearchIntentOutcome(SearchIntentStatus Status, SearchIntentResponse? Response = null, string? Message = null);
