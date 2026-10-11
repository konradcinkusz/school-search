namespace SchoolSearch.Reviews.Data;

/// <summary>DSA art. 16 notice of allegedly illegal content. Anonymous; the ack token is the only way back to its status.</summary>
public sealed class Notice
{
    public Guid Id { get; set; }

    /// <summary>The notified review, when the content is a review. Null for another content URL.</summary>
    public Guid? ReviewId { get; set; }

    /// <summary>URL or identifier of the notified content, up to 500 characters.</summary>
    public string ContentReference { get; set; } = "";

    /// <summary>Substantiated explanation, 20..3000 characters.</summary>
    public string Explanation { get; set; } = "";

    /// <summary>One of <see cref="NoticeCategories"/>.</summary>
    public string Category { get; set; } = "";

    /// <summary>Optional, up to 120 characters.</summary>
    public string? SubmitterName { get; set; }

    /// <summary>Optional, used only to notify the decision. Up to 200 characters.</summary>
    public string? SubmitterEmail { get; set; }

    /// <summary>Must be true for the notice to be accepted.</summary>
    public bool GoodFaithConfirmed { get; set; }

    /// <summary>One of <see cref="NoticeStatuses"/>.</summary>
    public string Status { get; set; } = NoticeStatuses.Received;

    /// <summary>Unguessable token for the status lookup.</summary>
    public string AckToken { get; set; } = "";

    public DateTimeOffset ReceivedAt { get; set; }

    public ModerationDecision? Decision { get; set; }
}
