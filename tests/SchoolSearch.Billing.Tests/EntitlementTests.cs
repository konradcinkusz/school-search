using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Subscriptions;
using static SchoolSearch.Billing.Tests.BillingFixtures;

namespace SchoolSearch.Billing.Tests;

/// <summary>The entitlement rule is tested through the real service and the real InMemory store.</summary>
public sealed class EntitlementTests
{
    private const string Slug = DefaultSlug;

    [Theory]
    [InlineData(SubscriptionStatuses.Active, true)]
    [InlineData(SubscriptionStatuses.Canceling, true)]
    [InlineData(SubscriptionStatuses.Pending, false)]
    [InlineData(SubscriptionStatuses.Expired, false)]
    public async Task Only_active_and_canceling_subscriptions_with_an_approved_owner_grant_premium(string status, bool expected)
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner, status));

        Assert.Equal(expected, await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_period_that_has_ended_grants_nothing_even_when_active()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var ended = SubscriptionOf(Slug, owner, SubscriptionStatuses.Active, DateTimeOffset.UtcNow.AddMinutes(-1));
        await factory.SeedAsync(ClaimOf(Slug, owner), ended);

        Assert.False(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_pending_claim_is_not_enough()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner, ClaimStatuses.Pending), SubscriptionOf(Slug, owner));

        Assert.False(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task The_approved_claim_must_belong_to_the_subscription_owner()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var someoneElse = factory.HashFor("someone-else");
        await factory.SeedAsync(ClaimOf(Slug, someoneElse), SubscriptionOf(Slug, owner));

        Assert.False(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_locked_profile_removes_premium_until_it_is_unlocked()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner), ProfileOf(Slug, locked: true));

        Assert.False(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Publishing_needs_premium_and_a_profile_row()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner));

        Assert.False(await PublishedAsync(factory));

        await factory.SeedAsync(ProfileOf(Slug));

        Assert.True(await PublishedAsync(factory));
    }

    [Fact]
    public async Task An_unknown_school_has_no_premium()
    {
        using var factory = new BillingApiFactory();

        Assert.False(await factory.IsPremiumAsync("nieznana-szkola", DateTimeOffset.UtcNow));
    }

    private static async Task<bool> PublishedAsync(BillingApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EntitlementService>()
            .IsPublishedAsync(Slug, DateTimeOffset.UtcNow);
    }
}
