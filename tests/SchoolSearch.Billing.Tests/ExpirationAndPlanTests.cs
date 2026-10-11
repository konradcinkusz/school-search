using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Models;
using SchoolSearch.Billing.Plans;
using SchoolSearch.Billing.Settings;
using SchoolSearch.Billing.Subscriptions;
using static SchoolSearch.Billing.Tests.BillingFixtures;
using static SchoolSearch.Billing.Tests.JsonTestHelpers;

namespace SchoolSearch.Billing.Tests;

public sealed class ExpirationTests
{
    private const string Slug = DefaultSlug;

    [Fact]
    public async Task The_expiry_pass_expires_a_lapsed_subscription_and_locks_the_profile_without_deleting_it()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var lapsed = SubscriptionOf(Slug, owner, SubscriptionStatuses.Active, DateTimeOffset.UtcNow.AddDays(-1));
        await factory.SeedAsync(ClaimOf(Slug, owner), lapsed, ProfileOf(Slug));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SubscriptionExpirationService>()
                .RunOnceAsync(CancellationToken.None);
        }

        var subscription = await factory.QueryAsync(db => db.Subscriptions.AsNoTracking().FirstAsync(s => s.Id == lapsed.Id));
        Assert.Equal(SubscriptionStatuses.Expired, subscription.Status);

        var profile = await factory.QueryAsync(db => db.PremiumProfiles.AsNoTracking().FirstAsync(p => p.SchoolSlug == Slug));
        Assert.True(profile.IsLocked);
        Assert.Equal(ProfileLocks.SubscriptionExpiredReason, profile.LockedReason);
        Assert.False(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task A_second_expiry_pass_changes_nothing()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner),
            SubscriptionOf(Slug, owner, SubscriptionStatuses.Canceling, DateTimeOffset.UtcNow.AddDays(-1)));

        using var scope = factory.Services.CreateScope();
        var expiry = scope.ServiceProvider.GetRequiredService<SubscriptionExpirationService>();
        await expiry.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, await expiry.RunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public void Unlocking_clears_the_lock_fields_and_keeps_the_profile()
    {
        var now = DateTimeOffset.UtcNow;
        var profile = ProfileOf("szkola-x", locked: true);

        ProfileLocks.Unlock(profile, now);

        Assert.False(profile.IsLocked);
        Assert.Null(profile.LockedReason);
        Assert.Null(profile.LockedAt);
        Assert.Equal("szkola-x", profile.SchoolSlug);
        Assert.Equal(now, profile.UpdatedAt);
    }
}

public sealed class PlanCatalogTests
{
    [Fact]
    public void An_empty_catalogue_is_valid_and_sells_nothing()
    {
        var catalog = PlanCatalog.FromOptions(Array.Empty<PlanOptions>());

        Assert.Empty(catalog.Plans);
        Assert.Null(catalog.Find("roczny"));
    }

    [Fact]
    public void A_plan_without_a_published_price_keeps_the_price_null()
    {
        var catalog = PlanCatalog.FromOptions(new[]
        {
            new PlanOptions { Code = "roczny", Name = "Roczny", Interval = PlanIntervals.Year },
        });

        var plan = catalog.Find("roczny");
        Assert.NotNull(plan);
        Assert.Null(plan!.PriceGrossPln);
        Assert.Equal(PlanIntervals.Year, plan.Interval);
    }

    [Fact]
    public void Duplicate_codes_stop_the_catalogue()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            PlanCatalog.FromOptions(new[]
            {
                new PlanOptions { Code = "plan", Name = "Jeden" },
                new PlanOptions { Code = "plan", Name = "Dwa" },
            });
        });
    }

    [Fact]
    public void An_unknown_interval_stops_the_catalogue()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            PlanCatalog.FromOptions(new[] { new PlanOptions { Code = "plan", Name = "Jeden", Interval = "week" } });
        });
    }

    [Fact]
    public void A_code_with_capitals_or_spaces_stops_the_catalogue()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            PlanCatalog.FromOptions(new[] { new PlanOptions { Code = "Plan One", Name = "Jeden" } });
        });
    }

    [Fact]
    public void A_negative_price_stops_the_catalogue()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            PlanCatalog.FromOptions(new[] { new PlanOptions { Code = "plan", Name = "Jeden", PriceGrossPln = -1m } });
        });
    }
}

public sealed class PlansEndpointTests
{
    [Fact]
    public async Task With_no_plans_configured_the_pricing_note_says_it_is_in_preparation()
    {
        using var factory = new BillingApiFactory(authConfigured: true, withPlans: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/plans");

        var root = await ReadJsonAsync(response);
        Assert.Equal(0, root.GetProperty("plans").GetArrayLength());
        Assert.Equal(PremiumCopy.PlansInPreparation, root.GetProperty("note").GetString());
        Assert.Equal("mock", root.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Configured_plans_are_listed_without_an_invented_price_and_with_the_mock_note()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/plans");

        var root = await ReadJsonAsync(response);
        var plan = root.GetProperty("plans")[0];
        Assert.Equal(BillingApiFactory.PlanCode, plan.GetProperty("code").GetString());
        Assert.Equal("month", plan.GetProperty("interval").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, plan.GetProperty("priceGrossPln").ValueKind);
        Assert.Equal(PremiumCopy.MockSalesNote, root.GetProperty("note").GetString());
    }
}
