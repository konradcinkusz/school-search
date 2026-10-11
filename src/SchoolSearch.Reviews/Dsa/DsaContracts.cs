namespace SchoolSearch.Reviews.Dsa;

// Request bodies. Everything is optional at the type level; DsaValidation decides what is required.

public sealed class DsaNoticeRequest
{
    public Guid? ReviewId { get; set; }

    public string? ContentReference { get; set; }

    public string? Explanation { get; set; }

    public string? Category { get; set; }

    public string? SubmitterName { get; set; }

    public string? SubmitterEmail { get; set; }

    public bool GoodFaithConfirmed { get; set; }
}

public sealed class DsaComplaintRequest
{
    public string? Explanation { get; set; }

    /// <summary>Notice acknowledgement token. Used instead of an account when the complainant is a notifier.</summary>
    public string? Token { get; set; }
}

public sealed class DsaDecisionRequest
{
    public string? Action { get; set; }

    public string? LegalOrTermsGround { get; set; }

    public string? Facts { get; set; }
}

public sealed class DsaComplaintDecisionRequest
{
    public string? Outcome { get; set; }

    public string? Facts { get; set; }
}

// Response bodies.

public sealed record NoticeReceipt(Guid Id, string AckToken, DateTimeOffset ReceivedAt);

/// <summary>Status of a notice for its notifier. Visible only with the acknowledgement token.</summary>
public sealed record NoticeStatusView(
    Guid Id,
    string Status,
    string Category,
    string ContentReference,
    Guid? ReviewId,
    DateTimeOffset ReceivedAt,
    StatementOfReasons? Decision);

/// <summary>DSA art. 17 statement of reasons. Automated is always false: no AI and no automated process decides.</summary>
public sealed record StatementOfReasons(
    Guid Id,
    string Action,
    string LegalOrTermsGround,
    string Facts,
    bool Automated,
    string RedressInfo,
    DateTimeOffset DecidedAt,
    Guid? ReviewId,
    Guid? NoticeId);

public sealed record ComplaintReceipt(Guid Id, Guid DecisionId, string Status, DateTimeOffset ReceivedAt, string AckToken);

public sealed record ComplaintOutcomeView(Guid Id, Guid DecisionId, string Status, StatementOfReasons Outcome);

/// <summary>AI advisory label as shown to moderators only. It orders the queue and never decides.</summary>
public sealed record ModerationTriageView(string Label, string Note, string Model, DateTimeOffset At);

/// <summary>One open item of the moderator queue. Kind is review, reply or notice.</summary>
public sealed record ModerationQueueItem(
    string Kind,
    Guid Id,
    Guid? ReviewId,
    string? SchoolSlug,
    DateTimeOffset CreatedAt,
    int? Rating,
    string? Title,
    string? Body,
    string? RepresentativeLabel,
    string? ReviewTitle,
    string? ReviewBody,
    string? Category,
    string? ContentReference,
    string? Explanation,
    ModerationTriageView? Triage);

public sealed record ModerationQueuePage(IReadOnlyList<ModerationQueueItem> Items, int Page, int PageSize, int Total);

public sealed record LegalTermsView(string Version, string Status, string Notice);

public sealed record LegalContactView(string? Email, string? Url, string Role, IReadOnlyList<string> Languages);

/// <summary>Computed counts only. No personal data, no per-school or per-author figures.</summary>
public sealed record TransparencyReport(
    DateTimeOffset GeneratedAt,
    TransparencyNotices Notices,
    TransparencyDecisions Decisions,
    TransparencyComplaints Complaints);

public sealed record TransparencyNotices(int Received, int Decided);

public sealed record TransparencyDecisions(int Total, int Automated, IReadOnlyDictionary<string, int> ByAction);

public sealed record TransparencyComplaints(int Received, int Upheld, int Reversed, int Pending);
