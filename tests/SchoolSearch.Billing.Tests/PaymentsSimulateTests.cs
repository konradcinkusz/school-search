using System.Net;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Payments;

namespace SchoolSearch.Billing.Tests;

public sealed class PaymentsSimulateTests
{
    [Fact]
    public async Task Simulate_is_forbidden_outside_mock_mode_for_any_caller()
    {
        using var factory = new PaymentsTestFactory(mockMode: false, secret: PaymentsTestFactory.Secret, environment: "Testing");
        var client = factory.CreateClient();
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, DateTimeOffset.UtcNow.AddDays(3));
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var response = await PaymentsTestSupport.SimulateAsync(client, sub.Id, "payment_succeeded", PaymentsTestSupport.OwnerSub);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await PaymentsTestSupport.ErrorCodeAsync(response));

        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.CountAsync());
        Assert.Equal(0, recorded);
    }

    [Fact]
    public async Task Simulate_runs_the_real_webhook_path_and_extends_the_owners_subscription()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var paidThrough = DateTimeOffset.UtcNow.AddDays(15);
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, paidThrough);
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var response = await PaymentsTestSupport.SimulateAsync(client, sub.Id, "payment_succeeded", PaymentsTestSupport.OwnerSub);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(paidThrough.AddMonths(1), stored.CurrentPeriodEnd);

        // The simulated event went through the same handler, so it is recorded like a provider event.
        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.AsNoTracking().ToListAsync());
        var single = Assert.Single(recorded);
        Assert.StartsWith("sim_", single.EventId, StringComparison.Ordinal);
        Assert.Equal(WebhookOutcomes.Applied, single.Outcome);
    }

    [Fact]
    public async Task Simulate_cannot_touch_another_owners_subscription()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var paidThrough = DateTimeOffset.UtcNow.AddDays(15);
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, paidThrough);
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var response = await PaymentsTestSupport.SimulateAsync(client, sub.Id, "payment_succeeded", PaymentsTestSupport.OtherSub);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(paidThrough, stored.CurrentPeriodEnd);
    }

    [Fact]
    public async Task Simulate_requires_a_signed_in_verified_user()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, DateTimeOffset.UtcNow.AddDays(3));
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var anonymous = await PaymentsTestSupport.SimulateAsync(client, sub.Id, "payment_succeeded", sub: null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var unverified = await PaymentsTestSupport.SimulateAsync(client, sub.Id, "payment_succeeded", PaymentsTestSupport.OwnerSub, verified: false);
        Assert.NotEqual(HttpStatusCode.OK, unverified.StatusCode);
    }

    [Fact]
    public async Task Simulate_rejects_event_names_outside_the_simulated_set()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, DateTimeOffset.UtcNow.AddDays(3));
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var response = await PaymentsTestSupport.SimulateAsync(client, sub.Id, "payment_teleported", PaymentsTestSupport.OwnerSub);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
