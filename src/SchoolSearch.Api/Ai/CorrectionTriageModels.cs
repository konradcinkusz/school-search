namespace SchoolSearch.Api.Ai;

/// <summary>
/// Closed vocabularies the model must choose from. Anything else in the model output rejects the whole answer.
/// Machine values are English; the Polish text lives only in the draft reply.
/// </summary>
public static class CorrectionTriageVocabulary
{
    public const string CategoryScoreDispute = "score_dispute";
    public const string ActionRouteToMethodology = "route_to_methodology";

    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
    {
        "source_error",
        "outdated",
        "typo",
        CategoryScoreDispute,
        "removal_request",
        "other",
    };

    public static readonly IReadOnlySet<string> Severities = new HashSet<string>(StringComparer.Ordinal)
    {
        "low",
        "medium",
        "high",
    };

    public static readonly IReadOnlySet<string> SuggestedActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "update_from_source",
        "request_source",
        ActionRouteToMethodology,
        "reject_out_of_scope",
        "no_change",
    };
}

/// <summary>
/// What the model may see of a correction: the school slug, the claimed field code and the submitter's free text
/// (the only text field a correction has). The reporter's contact e-mail is deliberately not part of this record.
/// </summary>
public sealed record CorrectionTriageInput(string SchoolSlug, string Field, string Note);

/// <summary>The grounding payload: exactly the values the model was given, serialised the same way it was sent.</summary>
public sealed record CorrectionTriageFacts(string SchoolSlug, string Field, string Note);

/// <summary>A validated model answer. Built only by <see cref="CorrectionTriageParser"/>.</summary>
public sealed record CorrectionTriageDraft(
    string Category,
    bool FieldLooksValid,
    string Severity,
    string SuggestedAction,
    string DraftReplyPl);

/// <summary>
/// The response of <c>POST /api/v1/admin/corrections/{id}/triage</c>. Not persisted: the moderator copies the draft
/// by hand. Nothing in the system sends it.
/// </summary>
public sealed record CorrectionTriageResponse(
    string Category,
    bool FieldLooksValid,
    string Severity,
    string SuggestedAction,
    string DraftReplyPl,
    string AiLabel,
    string Model);

public enum CorrectionTriageStatus
{
    Ok,
    NotFound,
    Unavailable,
    BudgetExhausted,
    RateLimited,
    InputRejected,
    OutputRejected,
}

/// <summary>Result of one triage call. <see cref="Response"/> is set only for <see cref="CorrectionTriageStatus.Ok"/>.</summary>
public sealed record CorrectionTriageOutcome(CorrectionTriageStatus Status, CorrectionTriageResponse? Response = null);
