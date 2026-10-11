namespace SchoolSearch.Billing.Data;

/// <summary>
/// A parent's enquiry to a school with a published premium profile. Personal data (RODO): purged after 90 days by
/// LeadPurgeService, never used for marketing, never shown publicly.
/// </summary>
public sealed class Lead
{
    public Guid Id { get; set; }

    public string SchoolSlug { get; set; } = "";

    /// <summary>Up to 200 characters.</summary>
    public string ParentEmail { get; set; } = "";

    /// <summary>Up to 1000 characters, plain text.</summary>
    public string Message { get; set; } = "";

    /// <summary>Version of the consent text the parent accepted, up to 50 characters.</summary>
    public string ConsentVersion { get; set; } = "";

    public DateTimeOffset ConsentAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>CreatedAt plus 90 days.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>One of <see cref="LeadStatuses"/>.</summary>
    public string Status { get; set; } = LeadStatuses.New;
}
