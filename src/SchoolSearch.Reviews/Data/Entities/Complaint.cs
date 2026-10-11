namespace SchoolSearch.Reviews.Data;

/// <summary>DSA art. 20 internal complaint against a moderation decision.</summary>
public sealed class Complaint
{
    public Guid Id { get; set; }

    public Guid DecisionId { get; set; }

    /// <summary>Up to 3000 characters.</summary>
    public string Explanation { get; set; } = "";

    /// <summary>Unguessable token for the status lookup.</summary>
    public string AckToken { get; set; } = "";

    /// <summary>One of <see cref="ComplaintStatuses"/>.</summary>
    public string Status { get; set; } = ComplaintStatuses.Received;

    public DateTimeOffset ReceivedAt { get; set; }
}
