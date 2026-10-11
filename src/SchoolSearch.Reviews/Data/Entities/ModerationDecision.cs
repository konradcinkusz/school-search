namespace SchoolSearch.Reviews.Data;

/// <summary>
/// A moderator's decision, which is also the statement of reasons under DSA art. 17. Written only by the
/// moderation path. <see cref="Automated"/> is always false: automated tools (including AI triage) never decide.
/// </summary>
public sealed class ModerationDecision
{
    public Guid Id { get; set; }

    public Guid? NoticeId { get; set; }

    public Guid? ReviewId { get; set; }

    /// <summary>One of <see cref="ModerationActions"/>.</summary>
    public string Action { get; set; } = "";

    /// <summary>The law or the term that was applied.</summary>
    public string LegalOrTermsGround { get; set; } = "";

    /// <summary>Facts and circumstances relied on.</summary>
    public string Facts { get; set; } = "";

    /// <summary>Always false. Enforced by a check constraint in the database.</summary>
    public bool Automated { get; set; }

    /// <summary>How the affected person can appeal (internal complaint, art. 20).</summary>
    public string RedressInfo { get; set; } = "";

    /// <summary>Hashed moderator sub. Never the raw sub.</summary>
    public string DecidedBy { get; set; } = "";

    public DateTimeOffset DecidedAt { get; set; }
}
