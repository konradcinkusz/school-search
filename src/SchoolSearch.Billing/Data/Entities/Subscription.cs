namespace SchoolSearch.Billing.Data;

/// <summary>
/// The paid plan of one school. One row per school: a re-purchase updates the row. Entitlement reads Status and
/// CurrentPeriodEnd; nothing is deleted on cancel or expiry.
/// </summary>
public sealed class Subscription
{
    public Guid Id { get; set; }

    public string SchoolSlug { get; set; } = "";

    /// <summary>Hash of the owner's subject (same rule as <see cref="SchoolClaim.OwnerHash"/>).</summary>
    public string OwnerHash { get; set; } = "";

    /// <summary>Code of a plan in the configured catalogue (Billing:Plans), up to 50 characters.</summary>
    public string PlanCode { get; set; } = "";

    /// <summary>One of <see cref="SubscriptionStatuses"/>.</summary>
    public string Status { get; set; } = SubscriptionStatuses.Pending;

    /// <summary>End of the paid period. Null until the first payment is confirmed.</summary>
    public DateTimeOffset? CurrentPeriodEnd { get; set; }

    /// <summary>True when the owner asked to stop at the end of the period. Status is then "canceling".</summary>
    public bool CancelAtPeriodEnd { get; set; }

    /// <summary>Opaque provider reference, up to 200 characters. Never interpreted by the entitlement rule.</summary>
    public string? ProviderRef { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
