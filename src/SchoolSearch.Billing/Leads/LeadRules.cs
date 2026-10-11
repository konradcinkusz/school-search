namespace SchoolSearch.Billing.Leads;

/// <summary>
/// Fixed product rules for parent enquiries (SPEC-F). These are rules, not configuration. Status strings match the
/// values F1 stores; they are repeated here as constants so this slice does not depend on F1's constant class names.
/// </summary>
public static class LeadRules
{
    /// <summary>Every enquiry is purged this many days after creation (RODO retention, SPEC-F).</summary>
    public const int RetentionDays = 90;

    public const int MinMessageLength = 10;
    public const int MaxMessageLength = 1000;
    public const int MaxEmailLength = 200;
    public const int MaxSlugLength = 200;

    /// <summary>The owner listing returns at most this many enquiries, newest first.</summary>
    public const int MaxListed = 200;

    /// <summary>Expired enquiries are deleted in batches of this size.</summary>
    public const int PurgeBatchSize = 500;

    public const string StatusNew = "new";
    public const string StatusRead = "read";

    public const string ClaimApproved = "approved";
    public const string SubscriptionActive = "active";
    public const string SubscriptionCanceling = "canceling";

    /// <summary>Authorization policy owned by F1 (authservice token, verified e-mail).</summary>
    public const string VerifiedUserPolicy = "VerifiedUser";

    public const string EnquiryRateLimitPolicy = "billing-leads-enquiries";
    public const string OwnerWriteRateLimitPolicy = "billing-leads-owner-writes";

    public const string SubjectClaim = "sub";
    public const string SubjectPepperKey = "Billing:SubjectPepper";
    public const string TrustProxyClientIpKey = "Billing:TrustProxyClientIpHeader";
    public const string BffSharedSecretKey = "BFF_SHARED_SECRET";
}

/// <summary>Configuration bound from Billing:Leads. Rate limits are clamped where they are read.</summary>
public sealed class LeadOptions
{
    public const string SectionName = "Billing:Leads";

    /// <summary>Parent enquiries per client IP per hour (SPEC-F: 5).</summary>
    public int EnquiriesPerIpPerHour { get; set; } = 5;

    /// <summary>Owner writes (enquiry delete) per subject per hour (SPEC-F authenticated writes: 20).</summary>
    public int OwnerWritesPerHour { get; set; } = 20;

    /// <summary>How often the purge service runs, in minutes.</summary>
    public int PurgeIntervalMinutes { get; set; } = 60;
}
