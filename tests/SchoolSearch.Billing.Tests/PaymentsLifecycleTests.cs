using System.Net;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Payments;

namespace SchoolSearch.Billing.Tests;

public sealed class PaymentsLifecycleTests
{
    private const string ExpiredReason = "subscription_expired";

    [Fact]
    public async Task Renewal_after_expiry_restores_the_period_from_the_payment_date_and_unlocks_the_profile()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Expired, now.AddDays(-10));
        await factory.UseDbAsync(db =>
        {
            db.Subscriptions.Add(sub);
            db.PremiumProfiles.Add(PaymentsTestSupport.NewProfile(isLocked: true, lockedReason: ExpiredReason));
            return db.SaveChangesAsync();
        });

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, PaymentsTestSupport.Event("evt-renew-1", "payment_succeeded", now, sub.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(SubscriptionStatuses.Active, stored.Status);
        Assert.Equal(now.AddMonths(1), stored.CurrentPeriodEnd);

        var profile = await factory.QueryDbAsync(db => db.PremiumProfiles.AsNoTracking().SingleAsync(p => p.SchoolSlug == PaymentsTestSupport.Slug));
        Assert.False(profile.IsLocked);
        Assert.Null(profile.LockedReason);
    }

    [Fact]
    public async Task Renewal_does_not_unlock_a_profile_locked_for_another_reason()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, now.AddDays(3));
        await factory.UseDbAsync(db =>
        {
            db.Subscriptions.Add(sub);
            db.PremiumProfiles.Add(PaymentsTestSupport.NewProfile(isLocked: true, lockedReason: "moderation_hold"));
            return db.SaveChangesAsync();
        });

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, PaymentsTestSupport.Event("evt-hold-1", "payment_succeeded", now, sub.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await factory.QueryDbAsync(db => db.PremiumProfiles.AsNoTracking().SingleAsync(p => p.SchoolSlug == PaymentsTestSupport.Slug));
        Assert.True(profile.IsLocked);
        Assert.Equal("moderation_hold", profile.LockedReason);
    }

    [Fact]
    public async Task Ended_event_expires_the_subscription_and_locks_the_profile_without_deleting_anything()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, now.AddDays(2), cancelAtPeriodEnd: true);
        await factory.UseDbAsync(db =>
        {
            db.Subscriptions.Add(sub);
            db.PremiumProfiles.Add(PaymentsTestSupport.NewProfile());
            return db.SaveChangesAsync();
        });

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, PaymentsTestSupport.Event("evt-ended-1", "subscription_ended", now, sub.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(SubscriptionStatuses.Expired, stored.Status);

        var profile = await factory.QueryDbAsync(db => db.PremiumProfiles.AsNoTracking().SingleAsync(p => p.SchoolSlug == PaymentsTestSupport.Slug));
        Assert.True(profile.IsLocked);
        Assert.Equal(ExpiredReason, profile.LockedReason);
        Assert.Equal(PaymentsTestSupport.Slug, profile.SchoolSlug);
    }

    [Fact]
    public async Task Renewal_failed_is_an_explicit_no_op_until_the_period_ends()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var paidThrough = now.AddDays(4);
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, paidThrough);
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, PaymentsTestSupport.Event("evt-rf-1", "renewal_failed", now, sub.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.Equal(SubscriptionStatuses.Active, stored.Status);
        Assert.Equal(paidThrough, stored.CurrentPeriodEnd);

        var recorded = await factory.QueryDbAsync(db => db.WebhookEvents.AsNoTracking().SingleAsync(e => e.EventId == "evt-rf-1"));
        Assert.Equal(WebhookOutcomes.Ignored, recorded.Outcome);
    }

    [Fact]
    public async Task Canceled_spelling_variant_maps_to_the_same_cancellation()
    {
        using var factory = new PaymentsTestFactory();
        var client = factory.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var sub = PaymentsTestSupport.NewSubscription(factory.HashFor(PaymentsTestSupport.OwnerSub), SubscriptionStatuses.Active, now.AddDays(9));
        await factory.UseDbAsync(db => { db.Subscriptions.Add(sub); return db.SaveChangesAsync(); });

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, PaymentsTestSupport.Event("evt-canceled-1", "subscription_canceled", now, sub.Id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await factory.QueryDbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.Id == sub.Id));
        Assert.True(stored.CancelAtPeriodEnd);
        Assert.Equal(SubscriptionStatuses.Canceling, stored.Status);
    }
}
