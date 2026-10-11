namespace SchoolSearch.Billing.Leads;

/// <summary>Enquiry body. Read camelCase and case-insensitive (web defaults). Classes, not records: binding stays simple.</summary>
public sealed class LeadEnquiryRequest
{
    public string? Email { get; set; }
    public string? Message { get; set; }
    public string? AcceptedConsentVersion { get; set; }

    /// <summary>Honeypot. Humans never see it; a non-empty value is accepted silently and nothing is stored.</summary>
    public string? Website { get; set; }
}

/// <summary>A validated enquiry. Text is cleaned (control characters removed, trimmed).</summary>
public sealed record LeadEnquiry(string Email, string Message);

/// <summary>A validated value or the error text. The error names the field only and never echoes submitted text.</summary>
public sealed record LeadValidated(LeadEnquiry? Value, string? Error);

public sealed record EnquiryReceipt(string Status);

public sealed record ConsentView(string Version, string Text, string Status);

/// <summary>Owner view of one enquiry. Only the school's approved representative ever receives this.</summary>
public sealed record LeadView(
    Guid Id,
    string ParentEmail,
    string Message,
    string ConsentVersion,
    DateTimeOffset ConsentAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string Status);

public sealed record LeadListView(IReadOnlyList<LeadView> Leads);

/// <summary>The uniform error shape: {"error":{"code","message","traceId"}}. Same shape as the other services, not referenced.</summary>
public sealed record LeadErrorEnvelope(LeadErrorBody Error);

public sealed record LeadErrorBody(string Code, string Message, string TraceId);
