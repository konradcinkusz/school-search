using SchoolSearch.Billing.Payments;

namespace SchoolSearch.Billing.Tests;

/// <summary>Pure unit tests: signature verification, period arithmetic and the event mapper. No host is started.</summary>
public sealed class PaymentsSignatureMapperTests
{
    private const string Secret = "unit-secret";
    private const string Body = "{\"id\":\"evt-1\"}";
    private static readonly string OwnerHash = new('a', 64);

    [Fact]
    public void Signature_verifies_the_exact_bytes_and_nothing_else()
    {
        var signature = WebhookSignature.Sign(Secret, Body);

        Assert.True(WebhookSignature.Verify(Secret, Body, signature));
        Assert.False(WebhookSignature.Verify(Secret, Body + " ", signature));
        Assert.False(WebhookSignature.Verify("other-secret", Body, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zz-not-hex")]
    [InlineData("abcd")]
    public void Malformed_or_truncated_signatures_fail_without_throwing(string? presented)
    {
        Assert.False(WebhookSignature.Verify(Secret, Body, presented));
    }

    [Fact]
    public void Signature_is_lowercase_hex_of_32_bytes()
    {
        var signature = WebhookSignature.Sign(Secret, Body);

        Assert.Equal(64, signature.Length);
        Assert.All(signature, c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'f')));
    }

    [Fact]
    public void Early_renewal_adds_one_period_to_the_remaining_days()
    {
        var paidAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var currentEnd = paidAt.AddDays(20);

        var result = PeriodMath.NewPeriodEnd(currentEnd, paidAt, providerEnd: null, interval: "month");

        Assert.Equal(currentEnd.AddMonths(1), result);
    }

    [Fact]
    public void Renewal_after_expiry_counts_from_the_payment_date()
    {
        var paidAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        var result = PeriodMath.NewPeriodEnd(paidAt.AddDays(-30), paidAt, providerEnd: null, interval: "year");

        Assert.Equal(paidAt.AddYears(1), result);
    }

    [Fact]
    public void Stale_provider_end_falls_back_to_the_single_period_grant()
    {
        var paidAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var currentEnd = paidAt.AddDays(20);
        var staleProviderEnd = paidAt.AddDays(5);

        var result = PeriodMath.NewPeriodEnd(currentEnd, paidAt, staleProviderEnd, "month");

        Assert.Equal(currentEnd.AddMonths(1), result);
    }

    [Fact]
    public void Missing_interval_without_a_fresh_provider_end_is_a_configuration_error()
    {
        var paidAt = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<InvalidOperationException>(() => PeriodMath.NewPeriodEnd(null, paidAt, null, interval: null));
    }

    [Theory]
    [InlineData("subscription_cancelled")]
    [InlineData("subscription_canceled")]
    [InlineData("SUBSCRIPTION_CANCELLED")]
    public void Cancelled_spellings_map_to_one_kind(string type)
    {
        var evt = Map($"{{\"id\":\"e\",\"type\":\"{type}\",\"occurred_at\":\"2026-10-11T12:00:00Z\"}}");

        Assert.Equal(BillingEventKind.SubscriptionCancelled, evt.Kind);
    }

    [Fact]
    public void Unknown_type_maps_to_unknown_and_keeps_the_raw_name()
    {
        var evt = Map("{\"id\":\"e\",\"type\":\"something_new\",\"occurred_at\":\"2026-10-11T12:00:00Z\"}");

        Assert.Equal(BillingEventKind.Unknown, evt.Kind);
        Assert.Equal("something_new", evt.RawType);
    }

    [Fact]
    public void Lifecycle_names_each_have_their_own_kind()
    {
        Assert.Equal(BillingEventKind.SubscriptionEnded, Kind("subscription_ended"));
        Assert.Equal(BillingEventKind.SubscriptionSuspended, Kind("subscription_suspended"));
        Assert.Equal(BillingEventKind.RenewalFailed, Kind("renewal_failed"));
        Assert.Equal(BillingEventKind.PaymentSucceeded, Kind("payment_succeeded"));
    }

    [Fact]
    public void Owner_hash_must_be_64_lowercase_hex()
    {
        var mapper = new GenericJsonEventMapper();
        var upper = "{\"id\":\"e\",\"type\":\"payment_succeeded\",\"occurred_at\":\"2026-10-11T12:00:00Z\",\"owner_hash\":\"" + new string('A', 64) + "\"}";
        var tooShort = "{\"id\":\"e\",\"type\":\"payment_succeeded\",\"occurred_at\":\"2026-10-11T12:00:00Z\",\"owner_hash\":\"abc\"}";

        Assert.Throws<BillingPayloadException>(() => mapper.Map(upper));
        Assert.Throws<BillingPayloadException>(() => mapper.Map(tooShort));
        Assert.Equal(OwnerHash, Map("{\"id\":\"e\",\"type\":\"payment_succeeded\",\"occurred_at\":\"2026-10-11T12:00:00Z\",\"owner_hash\":\"" + OwnerHash + "\"}").OwnerHash);
    }

    [Theory]
    [InlineData("{\"type\":\"payment_succeeded\",\"occurred_at\":\"2026-10-11T12:00:00Z\"}")]
    [InlineData("{\"id\":\"e\",\"occurred_at\":\"2026-10-11T12:00:00Z\"}")]
    [InlineData("{\"id\":\"e\",\"type\":\"payment_succeeded\"}")]
    [InlineData("{\"id\":\"e\",\"type\":\"payment_succeeded\",\"occurred_at\":\"2026-10-11T12:00:00Z\",\"subscription_id\":\"not-a-guid\"}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Malformed_events_are_rejected_with_a_safe_message(string body)
    {
        var mapper = new GenericJsonEventMapper();

        Assert.Throws<BillingPayloadException>(() => mapper.Map(body));
    }

    [Fact]
    public void Event_id_longer_than_the_storage_limit_is_rejected()
    {
        var mapper = new GenericJsonEventMapper();
        var body = "{\"id\":\"" + new string('x', GenericJsonEventMapper.MaxEventIdLength + 1) + "\",\"type\":\"payment_succeeded\",\"occurred_at\":\"2026-10-11T12:00:00Z\"}";

        Assert.Throws<BillingPayloadException>(() => mapper.Map(body));
    }

    private static BillingEvent Map(string body) => new GenericJsonEventMapper().Map(body);

    private static BillingEventKind Kind(string type) =>
        Map($"{{\"id\":\"e\",\"type\":\"{type}\",\"occurred_at\":\"2026-10-11T12:00:00Z\"}}").Kind;
}
