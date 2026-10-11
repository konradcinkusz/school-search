namespace SchoolSearch.Billing.Payments;

/// <summary>
/// The anti-corruption boundary for provider events. Implementations turn a verified raw body into a <see cref="BillingEvent"/>.
/// Only the webhook handler calls it. Provider names and DTOs never cross this boundary.
/// </summary>
public interface IBillingEventMapper
{
    /// <summary>Maps a body whose signature has already been verified. Throws <see cref="BillingPayloadException"/> when the body cannot be mapped.</summary>
    BillingEvent Map(string rawBody);
}

/// <summary>A verified body that is malformed or violates the event shape. The message is safe to return to the caller.</summary>
public sealed class BillingPayloadException : Exception
{
    public BillingPayloadException(string message) : base(message)
    {
    }
}
