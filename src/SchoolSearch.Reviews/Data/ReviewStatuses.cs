namespace SchoolSearch.Reviews.Data;

/// <summary>Status values of <see cref="Review.Status"/>. The database check constraint lists the same values.</summary>
public static class ReviewStatuses
{
    public const string Pending = "pending";
    public const string Published = "published";
    public const string Rejected = "rejected";
    public const string Removed = "removed";
}

/// <summary>Status values of <see cref="ReviewReply.Status"/>.</summary>
public static class ReplyStatuses
{
    public const string Pending = "pending";
    public const string Published = "published";
    public const string Rejected = "rejected";
}

/// <summary>Values of <see cref="ReviewReply.VerificationState"/>.</summary>
public static class VerificationStates
{
    public const string Unverified = "unverified";
    public const string VerifiedByModerator = "verified_by_moderator";
}

/// <summary>Values of <see cref="Notice.Status"/>.</summary>
public static class NoticeStatuses
{
    public const string Received = "received";
    public const string Decided = "decided";
}

/// <summary>Values of <see cref="Notice.Category"/> (DSA art. 16 notice categories).</summary>
public static class NoticeCategories
{
    public const string IllegalContent = "illegal_content";
    public const string PersonalData = "personal_data";
    public const string Defamation = "defamation";
    public const string Other = "other";
}

/// <summary>Values of <see cref="ModerationDecision.Action"/>.</summary>
public static class ModerationActions
{
    public const string Remove = "remove";
    public const string Restrict = "restrict";
    public const string Keep = "keep";
    public const string Publish = "publish";
    public const string Reject = "reject";
}

/// <summary>Values of <see cref="Complaint.Status"/>.</summary>
public static class ComplaintStatuses
{
    public const string Received = "received";
    public const string Upheld = "upheld";
    public const string Reversed = "reversed";
}

/// <summary>Values of <see cref="ModerationTriage.Label"/>. Advisory only.</summary>
public static class TriageLabels
{
    public const string Ok = "ok";
    public const string NeedsAttention = "needs_attention";
    public const string LikelyPersonalData = "likely_personal_data";
    public const string LikelyAbusive = "likely_abusive";
    public const string Unclear = "unclear";
}
