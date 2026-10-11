namespace SchoolSearch.Contracts.Public;

/// <summary>Response of <c>GET /api/v1/preschools</c>.</summary>
public sealed record PreschoolPage(
    IReadOnlyList<PreschoolListItem> Items,
    int Total,
    int Page,
    int PageSize);
