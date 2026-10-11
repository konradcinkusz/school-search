namespace SchoolSearch.Contracts.Public;

/// <summary>
/// One published user review. Reviews are user-generated content: they are never part of a score,
/// a ranking or an AI explanation, and they are always returned inside <see cref="ReviewsEnvelope"/>.
/// </summary>
public sealed record ReviewItem(
    Guid Id,
    int Rating,
    string Title,
    string Body,
    string SchoolYear,
    string ReviewedMonth,
    string AuthorLabel,
    ReviewReplyItem? Reply);

/// <summary>A published right-of-reply from the school. The verification state is shown next to it.</summary>
public sealed record ReviewReplyItem(string Body, string RepresentativeLabel, string VerificationState);

/// <summary>
/// Response of <c>GET /api/v1/schools/{slug}/reviews</c>. <c>UserGenerated</c> is always true and
/// <c>Notice</c> is the fixed Polish disclosure about how the authors were (not) verified.
/// </summary>
public sealed record ReviewsEnvelope(
    bool UserGenerated,
    string Notice,
    IReadOnlyList<ReviewItem> Items,
    int Total,
    int Page,
    int PageSize);
