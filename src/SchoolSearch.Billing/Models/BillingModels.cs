namespace SchoolSearch.Billing.Models;

/// <summary>Fixed copy shown next to sponsored content. Changing the label is a product decision, not a config change.</summary>
public static class PremiumCopy
{
    public const string SponsoredLabel = "Treść sponsorowana";
    public const string PlansInPreparation = "Cennik w przygotowaniu.";
    public const string MockSalesNote = "Sprzedaż w trybie testowym.";
}

/// <summary>Body of <c>GET /api/v1/plans</c>. An empty list comes with the note "Cennik w przygotowaniu.".</summary>
public sealed record PlansResponse(IReadOnlyList<PlanItem> Plans, string? Note, string Mode);

public sealed record PlanItem(string Code, string Name, decimal? PriceGrossPln, string Interval);

/// <summary>
/// Body of <c>GET /api/v1/schools/{slug}/premium</c>. Anonymous. The sponsored block is always labelled and never
/// carries score, rank or order. <see cref="Profile"/> is present only when premium holds and a profile exists.
/// </summary>
public sealed record PremiumEnvelope(
    bool Sponsored,
    string Tier,
    bool EditorsVerified,
    PremiumProfileItem? Profile,
    string Label,
    bool EnquiriesOpen);

public sealed record PremiumProfileItem(string Headline, string Description, string? WebsiteUrl);

/// <summary>Body of <c>PUT /api/v1/schools/{slug}/premium</c>.</summary>
public sealed record PremiumProfileRequest(string? Headline, string? Description, string? WebsiteUrl);

/// <summary>Body of <c>POST /api/v1/schools/{slug}/claims</c>.</summary>
public sealed record ClaimRequest(string? RoleDescription);

/// <summary>Receipt of a claim write, and of a moderator decision.</summary>
public sealed record ClaimReceipt(Guid Id, string SchoolSlug, string Status);

/// <summary>Body of <c>POST /api/v1/subscriptions/checkout</c>.</summary>
public sealed record CheckoutRequest(string? SchoolSlug, string? PlanCode);

public sealed record CheckoutReceipt(Guid SubscriptionId, string PaymentUrl);

/// <summary>The owner's view of a claim. Moderator notes are never included.</summary>
public sealed record OwnClaimItem(
    Guid Id,
    string SchoolSlug,
    string RoleDescription,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

/// <summary>The owner's view of a subscription.</summary>
public sealed record SubscriptionItem(
    Guid Id,
    string SchoolSlug,
    string PlanCode,
    string Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

/// <summary>Body of <c>GET /api/v1/claims/mine</c>.</summary>
public sealed record MyClaimsResponse(IReadOnlyList<OwnClaimItem> Claims, IReadOnlyList<SubscriptionItem> Subscriptions);

/// <summary>Body of <c>GET /api/v1/moderation/claims</c>. Moderator view: includes the internal note.</summary>
public sealed record ModerationClaimsResponse(IReadOnlyList<ModerationClaimItem> Items);

public sealed record ModerationClaimItem(
    Guid Id,
    string SchoolSlug,
    string RoleDescription,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? Notes);

/// <summary>Body of <c>POST /api/v1/moderation/claims/{id}/decision</c>.</summary>
public sealed record ModerationDecisionRequest(string? Action, string? Notes);

/// <summary>The decisions a moderator can take on a claim.</summary>
public static class ClaimActions
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Revoke = "revoke";
}
