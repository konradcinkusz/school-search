namespace SchoolSearch.Billing.Payments;

/// <summary>The lifecycle meaning of a provider event, after the anti-corruption mapping (P11). Nothing downstream sees provider names.</summary>
public enum BillingEventKind
{
    /// <summary>Not recognised. Logged and recorded with outcome unknown_type.</summary>
    Unknown = 0,
    PaymentSucceeded,
    SubscriptionCancelled,
    SubscriptionEnded,
    SubscriptionSuspended,
    RenewalFailed,
}

/// <summary>
/// The normalised event the handler consumes. Amount and currency are deliberately absent: entitlement never depends
/// on money fields, and plans are resolved server-side from PlanCode.
/// </summary>
public sealed record BillingEvent(
    string EventId,
    string RawType,
    BillingEventKind Kind,
    DateTimeOffset OccurredAt,
    Guid? SubscriptionId,
    string? ProviderSubscriptionRef,
    string? SchoolSlug,
    string? OwnerHash,
    string? PlanCode,
    DateTimeOffset? PeriodEnd);
