namespace SchoolSearch.Contracts.Public;

/// <summary>Response of <c>GET /api/v1/compare</c>: between two and four schools, in the requested order.</summary>
public sealed record ComparePage(IReadOnlyList<SchoolProfile> Items);
