namespace SchoolSearch.Reviews.Models;

/// <summary>Reply to a write: the review id and its new status (pending for new reviews, replies and removals).</summary>
public sealed record ReviewReceipt(Guid Id, string Status);

/// <summary>Body of <c>POST /api/v1/schools/{slug}/reviews</c>.</summary>
public sealed record CreateReviewRequest(
    int Rating,
    string? Title,
    string? Body,
    int SchoolYearFrom,
    string? AcceptedTermsVersion);

/// <summary>Body of <c>POST /api/v1/reviews/{id}/reply</c>.</summary>
public sealed record ReviewReplyRequest(string? Body, string? RepresentativeLabel);

/// <summary>Response of <c>GET /api/v1/reviews/mine</c>. Only the author sees this shape.</summary>
public sealed record MyReviewsResponse(IReadOnlyList<OwnReviewItem> Items);

/// <summary>One of the author's own reviews, with its status and any statements of reasons (DSA art. 17).</summary>
public sealed record OwnReviewItem(
    Guid Id,
    string SchoolSlug,
    int Rating,
    string Title,
    string Body,
    string SchoolYear,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    OwnReplyItem? Reply,
    IReadOnlyList<StatementOfReasonsItem> Statements);

/// <summary>The author's view of the school's reply, including its moderation status.</summary>
public sealed record OwnReplyItem(string Body, string RepresentativeLabel, string VerificationState, string Status);

/// <summary>A moderator decision shown to the affected author (DSA art. 17). Never contains the moderator's identity.</summary>
public sealed record StatementOfReasonsItem(
    Guid DecisionId,
    string Action,
    string LegalOrTermsGround,
    string Facts,
    bool Automated,
    string RedressInfo,
    DateTimeOffset DecidedAt);

/// <summary>Validated review input, trimmed.</summary>
public sealed record ReviewDraft(int Rating, string Title, string Body, int SchoolYearFrom);

/// <summary>Validated reply input, trimmed.</summary>
public sealed record ReplyDraft(string Body, string RepresentativeLabel);

/// <summary>Fixed public copy. The notice is the disclosure the UI must show next to every review list.</summary>
public static class ReviewsPublicCopy
{
    public const string UserGeneratedNotice =
        "Opinie pochodzą od zalogowanych użytkowników z potwierdzonym adresem e-mail. " +
        "Nie weryfikujemy, czy autor jest rodzicem ucznia tej szkoły. Opinie nie wpływają na wynik szkoły.";

    public const string AuthorLabel = "Rodzic/opiekun (niezweryfikowany)";
}
