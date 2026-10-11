namespace SchoolSearch.Contracts.Public;

/// <summary>
/// Response of <c>GET /api/v1/ranking</c>. Descending only: there is no sort or direction parameter, and suppressed
/// schools are never placed in the ranking (<see cref="UnrankedCount"/> counts them).
/// </summary>
public sealed record RankingPage(
    string Scope,
    string? GminaTeryt,
    int? ExamYear,
    string MethodologyVersion,
    IReadOnlyList<RankedItem> Items,
    int UnrankedCount,
    int Total,
    int Page,
    int PageSize);

/// <summary>One published, ranked school. Ties share a competition rank (1, 1, 3).</summary>
public sealed record RankedItem(int Rank, string Slug, string Name, string? GminaName, ScoreBlock Score);
