using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolSearch.Billing.Payments;

/// <summary>
/// PLACEHOLDER mapper for a provider-neutral JSON event shape. It is the only mapper shipped. A real provider adapter
/// replaces it behind <see cref="IBillingEventMapper"/> once the provider is chosen (provider decision still open).
/// Wire format (snake_case, names fixed by the attributes):
/// id (required), type (required), occurred_at (required), subscription_id, provider_subscription_ref, school_slug,
/// owner_hash (64 lowercase hex), plan_code, period_end (optional provider period end).
/// Accepted type names: payment_succeeded, subscription_cancelled (or subscription_canceled), subscription_ended,
/// subscription_suspended, renewal_failed. Anything else maps to Unknown.
/// </summary>
public sealed class GenericJsonEventMapper : IBillingEventMapper
{
    public const int MaxEventIdLength = 200;
    public const int MaxTypeLength = 100;
    public const int MaxProviderRefLength = 200;
    public const int MaxSlugLength = 200;
    public const int MaxPlanCodeLength = 50;
    public const int OwnerHashLength = 64;

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    public BillingEvent Map(string rawBody)
    {
        WireEvent? wire;
        try
        {
            wire = JsonSerializer.Deserialize<WireEvent>(rawBody, ReadOptions);
        }
        catch (JsonException)
        {
            throw new BillingPayloadException("Body is not valid JSON.");
        }

        if (wire is null)
        {
            throw new BillingPayloadException("Body is required.");
        }

        var eventId = Required(wire.Id, MaxEventIdLength, "id");
        var rawType = Required(wire.Type, MaxTypeLength, "type");
        var occurredAt = wire.OccurredAt ?? throw new BillingPayloadException("occurred_at is required.");

        Guid? subscriptionId = null;
        if (!string.IsNullOrWhiteSpace(wire.SubscriptionId))
        {
            if (!Guid.TryParse(wire.SubscriptionId, out var parsed))
            {
                throw new BillingPayloadException("subscription_id is not a valid identifier.");
            }

            subscriptionId = parsed;
        }

        var ownerHash = Optional(wire.OwnerHash, OwnerHashLength, "owner_hash");
        if (ownerHash is not null && !IsLowerHex(ownerHash))
        {
            throw new BillingPayloadException("owner_hash must be 64 lowercase hex characters.");
        }

        return new BillingEvent(
            EventId: eventId,
            RawType: rawType,
            Kind: Classify(rawType),
            OccurredAt: occurredAt,
            SubscriptionId: subscriptionId,
            ProviderSubscriptionRef: Optional(wire.ProviderSubscriptionRef, MaxProviderRefLength, "provider_subscription_ref"),
            SchoolSlug: Optional(wire.SchoolSlug, MaxSlugLength, "school_slug"),
            OwnerHash: ownerHash,
            PlanCode: Optional(wire.PlanCode, MaxPlanCodeLength, "plan_code"),
            PeriodEnd: wire.PeriodEnd);
    }

    /// <summary>
    /// Explicit mapping of every known lifecycle name. Spelling variants are absorbed here. Unknown names fall through to
    /// Unknown, which the handler logs and records.
    /// </summary>
    internal static BillingEventKind Classify(string rawType) => rawType.ToLowerInvariant() switch
    {
        "payment_succeeded" => BillingEventKind.PaymentSucceeded,
        "subscription_cancelled" or "subscription_canceled" => BillingEventKind.SubscriptionCancelled,
        "subscription_ended" => BillingEventKind.SubscriptionEnded,
        "subscription_suspended" => BillingEventKind.SubscriptionSuspended,
        "renewal_failed" => BillingEventKind.RenewalFailed,
        _ => BillingEventKind.Unknown,
    };

    private static string Required(string? value, int maxLength, string field)
    {
        var trimmed = Optional(value, maxLength, field);
        return trimmed ?? throw new BillingPayloadException($"{field} is required.");
    }

    private static string? Optional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new BillingPayloadException($"{field} is too long.");
        }

        return trimmed;
    }

    private static bool IsLowerHex(string value) =>
        value.Length == OwnerHashLength && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>Wire shape. Unknown JSON properties are ignored; provider casing is handled by the attributes.</summary>
    private sealed class WireEvent
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("occurred_at")]
        public DateTimeOffset? OccurredAt { get; init; }

        [JsonPropertyName("subscription_id")]
        public string? SubscriptionId { get; init; }

        [JsonPropertyName("provider_subscription_ref")]
        public string? ProviderSubscriptionRef { get; init; }

        [JsonPropertyName("school_slug")]
        public string? SchoolSlug { get; init; }

        [JsonPropertyName("owner_hash")]
        public string? OwnerHash { get; init; }

        [JsonPropertyName("plan_code")]
        public string? PlanCode { get; init; }

        [JsonPropertyName("period_end")]
        public DateTimeOffset? PeriodEnd { get; init; }
    }
}
