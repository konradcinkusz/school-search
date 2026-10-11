namespace SchoolSearch.Contracts.Public;

/// <summary>Response of <c>GET /api/v1/schools</c>.</summary>
public sealed record SchoolPage(
    IReadOnlyList<SchoolListItem> Items,
    int Page,
    int PageSize,
    int Total);
