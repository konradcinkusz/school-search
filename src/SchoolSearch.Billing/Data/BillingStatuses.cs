namespace SchoolSearch.Billing.Data;

/// <summary>Values of <see cref="SchoolClaim.Status"/>. The database check constraint lists the same values.</summary>
public static class ClaimStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Revoked = "revoked";

    public static readonly string[] All = [Pending, Approved, Rejected, Revoked];
}

/// <summary>Values of <see cref="Subscription.Status"/>.</summary>
public static class SubscriptionStatuses
{
    /// <summary>Checkout started, no payment confirmed yet. Grants nothing.</summary>
    public const string Pending = "pending";

    /// <summary>Paid and running. Grants premium until CurrentPeriodEnd.</summary>
    public const string Active = "active";

    /// <summary>Paid, cancel at period end requested. Still grants premium until CurrentPeriodEnd.</summary>
    public const string Canceling = "canceling";

    /// <summary>Period ended without renewal. Grants nothing; the profile is locked, never deleted.</summary>
    public const string Expired = "expired";
}

/// <summary>Values of <see cref="WebhookEvent.Outcome"/>.</summary>
public static class WebhookOutcomes
{
    public const string Applied = "applied";
    public const string Ignored = "ignored";
    public const string UnknownType = "unknown_type";
    public const string OrderSynthesised = "order_synthesised";
}

/// <summary>Values of <see cref="Lead.Status"/>.</summary>
public static class LeadStatuses
{
    public const string New = "new";
    public const string Read = "read";
}

/// <summary>Plan intervals accepted in configuration.</summary>
public static class PlanIntervals
{
    public const string Month = "month";
    public const string Year = "year";
}
