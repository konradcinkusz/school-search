using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Subscriptions;
using static SchoolSearch.Billing.Tests.BillingFixtures;
using static SchoolSearch.Billing.Tests.JsonTestHelpers;

namespace SchoolSearch.Billing.Tests;

public sealed class SubscriptionEndpointTests
{
    private const string Slug = DefaultSlug;

    [Fact]
    public async Task Checkout_without_an_approved_claim_answers_403()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/subscriptions/checkout", Owner,
            content: JsonBody(new { schoolSlug = Slug, planCode = BillingApiFactory.PlanCode }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("claim_not_approved", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Checkout_with_an_unknown_plan_answers_400()
    {
        using var factory = new BillingApiFactory();
        await factory.SeedAsync(ClaimOf(Slug, factory.HashFor(Owner)));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/subscriptions/checkout", Owner,
            content: JsonBody(new { schoolSlug = Slug, planCode = "nie-ma-takiego" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_plan", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Checkout_refuses_every_plan_while_the_catalogue_is_empty()
    {
        using var factory = new BillingApiFactory(authConfigured: true, withPlans: false);
        await factory.SeedAsync(ClaimOf(Slug, factory.HashFor(Owner)));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/subscriptions/checkout", Owner,
            content: JsonBody(new { schoolSlug = Slug, planCode = BillingApiFactory.PlanCode }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown_plan", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Checkout_in_mock_mode_creates_a_pending_subscription_and_a_synthetic_url_that_grants_nothing()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/subscriptions/checkout", Owner,
            content: JsonBody(new { schoolSlug = Slug, planCode = BillingApiFactory.PlanCode }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        var subscriptionId = root.GetProperty("subscriptionId").GetGuid();
        Assert.Equal($"{BillingApiFactory.CheckoutBase}/mock-checkout/{subscriptionId:D}", root.GetProperty("paymentUrl").GetString());

        var stored = await factory.QueryAsync(db => db.Subscriptions.AsNoTracking().FirstAsync(s => s.Id == subscriptionId));
        Assert.Equal(SubscriptionStatuses.Pending, stored.Status);
        Assert.Equal(BillingApiFactory.PlanCode, stored.PlanCode);
        Assert.Equal(owner, stored.OwnerHash);
        Assert.False(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Checkout_is_refused_while_a_paid_period_runs()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/subscriptions/checkout", Owner,
            content: JsonBody(new { schoolSlug = Slug, planCode = BillingApiFactory.PlanCode }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("subscription_active", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Another_owner_cannot_buy_a_plan_for_a_school_someone_else_subscribes_to()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var other = factory.HashFor("someone-else");
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, other, SubscriptionStatuses.Expired));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, "/api/v1/subscriptions/checkout", Owner,
            content: JsonBody(new { schoolSlug = Slug, planCode = BillingApiFactory.PlanCode }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Cancel_sets_the_flag_and_keeps_the_row_and_the_access_until_the_period_ends()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var subscription = SubscriptionOf(Slug, owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), subscription);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{subscription.Id}/cancel", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.Equal("canceling", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("cancelAtPeriodEnd").GetBoolean());

        var stored = await factory.QueryAsync(db => db.Subscriptions.AsNoTracking().FirstAsync(s => s.Id == subscription.Id));
        Assert.Equal(SubscriptionStatuses.Canceling, stored.Status);
        Assert.True(await factory.IsPremiumAsync(Slug, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Cancelling_twice_is_harmless()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var subscription = SubscriptionOf(Slug, owner, SubscriptionStatuses.Canceling);
        await factory.SeedAsync(ClaimOf(Slug, owner), subscription);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{subscription.Id}/cancel", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("canceling", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelling_a_pending_subscription_answers_409()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var subscription = SubscriptionOf(Slug, owner, SubscriptionStatuses.Pending);
        await factory.SeedAsync(ClaimOf(Slug, owner), subscription);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{subscription.Id}/cancel", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_state", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Cancelling_someone_elses_subscription_looks_like_a_missing_one()
    {
        using var factory = new BillingApiFactory();
        var subscription = SubscriptionOf(Slug, factory.HashFor(Owner));
        await factory.SeedAsync(subscription);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{subscription.Id}/cancel", "intruz");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("subscription_not_found", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Cancel_without_a_token_answers_401()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{Guid.NewGuid()}/cancel");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resume_clears_the_cancel_flag()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var subscription = SubscriptionOf(Slug, owner, SubscriptionStatuses.Canceling);
        await factory.SeedAsync(ClaimOf(Slug, owner), subscription);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{subscription.Id}/resume", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.Equal("active", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("cancelAtPeriodEnd").GetBoolean());
    }

    [Fact]
    public async Task Resume_after_the_period_has_ended_answers_409()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        var subscription = SubscriptionOf(Slug, owner, SubscriptionStatuses.Canceling, DateTimeOffset.UtcNow.AddMinutes(-1));
        await factory.SeedAsync(ClaimOf(Slug, owner), subscription);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/subscriptions/{subscription.Id}/resume", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("period_ended", await ErrorCodeAsync(response));
    }
}
