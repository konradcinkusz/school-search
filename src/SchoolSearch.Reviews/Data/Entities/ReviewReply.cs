namespace SchoolSearch.Reviews.Data;

/// <summary>
/// The school's right of reply to a review. One per review (primary key is the review id). Public only once published.
/// <see cref="VerificationState"/> is "unverified" unless a moderator has verified the representative.
/// </summary>
public sealed class ReviewReply
{
    public Guid ReviewId { get; set; }

    /// <summary>10..2000 characters, plain text.</summary>
    public string Body { get; set; } = "";

    /// <summary>Self-declared role of the replying person, for example "Dyrekcja szkoły".</summary>
    public string RepresentativeLabel { get; set; } = "";

    /// <summary>One of <see cref="VerificationStates"/>.</summary>
    public string VerificationState { get; set; } = VerificationStates.Unverified;

    public string AuthorHash { get; set; } = "";

    /// <summary>One of <see cref="ReplyStatuses"/>.</summary>
    public string Status { get; set; } = ReplyStatuses.Pending;

    public DateTimeOffset CreatedAt { get; set; }
}
