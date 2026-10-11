using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Payments;

namespace SchoolSearch.Billing.Tests;

public sealed class PaymentsWebhookTests
{
    private const string WrongSignatureHex = "00000000000000000000000000000000000000000000000000000000000000ab";

    [Fact]
    public async Task Redelivered_payment_extends_the_period_once()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var paidThrough = now.AddDays(20);
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, paidThrough);
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        var payload = PaymentsTestSupport.Event("evt-double-1", "payment_succeeded", now, sub.Id);

        using var first = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(await PaymentsTestSupport.DuplicateFlagAsync(first));

        using var second = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(await PaymentsTestSupport.DuplicateFlagAsync(second));

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(paidThrough.AddMonths(1), stored.CurrentPeriodEnd);

        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.CountAsync(e => e.EventId == "evt-double-1"));
        Assert.Equal(1, recorded);
    }

    [Fact]
    public async Task Fresh_provider_period_end_is_used_when_later_than_the_base()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, now.AddDays(20));
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        var providerEnd = now.AddDays(60);
        var payload = PaymentsTestSupport.Event("evt-fresh-end-1", "payment_succeeded", now, sub.Id, periodEnd: providerEnd);
        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(providerEnd, stored.CurrentPeriodEnd);
    }

    [Fact]
    public async Task Cancel_only_sets_the_flag_and_deletes_nothing()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var paidThrough = now.AddDays(12);
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, paidThrough);
        await factory.UseDbAsync(db =>
        {
            db.Subscriptions.Add(sub);
            db.PremiumProfiles.Add(PaymentsTestSupport.NewProfile());
            return db.SaveChangesAsync();
        });

        var payload = PaymentsTestSupport.Event("evt-cancel-1", "subscription_cancelled", now, sub.Id);
        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.True(stored.CancelAtPeriodEnd);
        Assert.Equal(SubscriptionStatuses.Canceling, stored.Status);
        Assert.Equal(paidThrough, stored.CurrentPeriodEnd);

        var profile = await factory.QueryDbAsync(db => db.PremiumProfiles.AsNoTracking().SingleAsync(p => p.SchoolSlug == PaymentsTestSupport.Slug));
        Assert.False(profile.IsLocked);
    }

    [Fact]
    public async Task Unknown_event_type_is_recorded_as_unknown_and_changes_nothing()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var paidThrough = now.AddDays(5);
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, paidThrough);
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        var payload = PaymentsTestSupport.Event("evt-unknown-1", "invoice_something_new", now, sub.Id);
        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == "evt-unknown-1"));
        Assert.Equal(WebhookOutcomes.UnknownType, recorded.Outcome);
        Assert.Equal("invoice_something_new", recorded.Type);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(paidThrough, stored.CurrentPeriodEnd);
        Assert.Equal(SubscriptionStatuses.Active, stored.Status);
    }

    [Fact]
    public async Task Payment_that_beats_checkout_synthesises_the_subscription()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var subscriptionId = Guid.NewGuid();
        var payload = PaymentsTestSupport.Event(
            "evt-synth-1",
            "payment_succeeded",
            now,
            subscriptionId,
            PaymentsTestSupport.Slug,
            factory.HashFor(PaymentsTestSupport.OwnerSub),
            "yearly");

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == subscriptionId));
        Assert.Equal(SubscriptionStatuses.Active, created.Status);
        Assert.Equal("yearly", created.PlanCode);
        Assert.Equal(now.AddYears(1), created.CurrentPeriodEnd);

        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == "evt-synth-1"));
        Assert.Equal(WebhookOutcomes.OrderSynthesised, recorded.Outcome);
    }

    [Fact]
    public async Task Payment_without_a_match_and_without_metadata_is_recorded_not_invented()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();

        var payload = PaymentsTestSupport.Event("evt-orphan-1", "payment_succeeded", DateTimeOffset.UtcNow);
        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var subscriptions = await factory.QueryDbAsync(db => db.Subscriptions.CountAsync());
        Assert.Equal(0, subscriptions);

        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == "evt-orphan-1"));
        Assert.Equal(WebhookOutcomes.Ignored, recorded.Outcome);
    }

    [Fact]
    public async Task Unsigned_webhook_is_rejected_in_production_even_with_a_guessed_secret()
    {
        using var factory = new PaymentsTestFactory(mockMode: false, secret: null, environment: "Production");
        var client = factory.CreateClient();
        var payload = PaymentsTestSupport.Event("evt-prod-1", "payment_succeeded", DateTimeOffset.UtcNow);

        using var unsigned = await PaymentsTestSupport.PostWebhookAsync(client, payload, secret: null);
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal("invalid_signature", await PaymentsTestSupport.ErrorCodeAsync(unsigned));

        using var guessed = await PaymentsTestSupport.PostWebhookAsync(client, payload, secret: "guessed-secret");
        Assert.Equal(HttpStatusCode.Unauthorized, guessed.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.WebhookEvents.CountAsync());
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task Unsigned_webhook_is_accepted_in_mock_mode_without_a_secret()
    {
        using var factory = new PaymentsTestFactory(mockMode: true, secret: null);
        var client = factory.CreateClient();
        var payload = PaymentsTestSupport.Event("evt-mock-unsigned-1", "invoice_something_new", DateTimeOffset.UtcNow);

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload, secret: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_signature_is_rejected_when_a_secret_is_configured_and_nothing_is_written()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var payload = PaymentsTestSupport.Event("evt-forged-1", "payment_succeeded", DateTimeOffset.UtcNow);
        var raw = JsonSerializer.Serialize(payload, PaymentsTestSupport.Json);

        using var request = new HttpRequestMessage(HttpMethod.Post, PaymentsTestSupport.WebhookPath)
        {
            Content = new StringContent(raw, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Signature", WrongSignatureHex);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var stored = await factory.QueryDbAsync(db => db.WebhookEvents.CountAsync());
        Assert.Equal(0, stored);
    }

    [Fact]
    public async Task Malformed_body_with_a_valid_signature_is_a_400_and_not_a_500()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();

        using var response = await PaymentsTestSupport.PostRawWebhookAsync(client, "{not json");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_body", await PaymentsTestSupport.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Event_without_an_id_is_a_400()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var payload = new Dictionary<string, object?>
        {
            ["type"] = "payment_succeeded",
            ["occurred_at"] = DateTimeOffset.UtcNow,
        };

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
