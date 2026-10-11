namespace SchoolSearch.Billing.Data;

/// <summary>
/// The owner-written sponsored block of one school ("Treść sponsorowana"). Never affects score, ranking or order.
/// Publishable only while the entitlement rule holds; IsLocked is set by expiry and cleared by renewal.
/// </summary>
public sealed class PremiumProfile
{
    /// <summary>Slug of the school (primary key).</summary>
    public string SchoolSlug { get; set; } = "";

    /// <summary>Plain text, 1 to 120 characters.</summary>
    public string Headline { get; set; } = "";

    /// <summary>Plain text, 1 to 1500 characters.</summary>
    public string Description { get; set; } = "";

    /// <summary>Absolute https URL, up to 300 characters, or null.</summary>
    public string? WebsiteUrl { get; set; }

    public bool IsLocked { get; set; }

    /// <summary>Why the profile is locked, up to 100 characters (for example "subscription_expired").</summary>
    public string? LockedReason { get; set; }

    public DateTimeOffset? LockedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
