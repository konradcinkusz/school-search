namespace SchoolSearch.Billing.Subscriptions;

/// <summary>What the checkout endpoint hands to the payment provider to create a hosted checkout.</summary>
public sealed record CheckoutSessionRequest(Guid SubscriptionId, string SchoolSlug, string PlanCode);

/// <summary>
/// The provider's hosted checkout (card data never touches this service). The Payments subfolder (F2) registers the real
/// implementation; the default registered here is unconfigured, so outside mock mode checkout answers 503 billing_unavailable (P8).
/// </summary>
public interface ICheckoutGateway
{
    /// <summary>True when this gateway can create checkouts. Checked before any row is written.</summary>
    bool IsConfigured { get; }

    /// <summary>The hosted checkout URL, or null when the provider cannot create one now.</summary>
    Task<string?> CreateCheckoutUrlAsync(CheckoutSessionRequest request, CancellationToken ct);
}

/// <summary>Default gateway: no provider is configured.</summary>
public sealed class UnconfiguredCheckoutGateway : ICheckoutGateway
{
    public bool IsConfigured => false;

    public Task<string?> CreateCheckoutUrlAsync(CheckoutSessionRequest request, CancellationToken ct) =>
        Task.FromResult<string?>(null);
}
