using System.Net;
using System.Net.Http;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.DevSeed;
using Microsoft.EntityFrameworkCore;

namespace SchoolSearch.Billing.Tests.DevSeed;

/// <summary>POST /dev/seed through the real host (InMemory store). Gate order and the Production case are also unit-tested.</summary>
public sealed class DevSeedEndpointTests
{
    private const string SeedUrl = "/dev/seed";
    private const string OwnerSub = "owner-devseed-sub";
    private const string ModeratorSub = "moderator-devseed-sub";
    private static readonly RowCounts Expected = new(Claims: 2, Subscriptions: 1, Profiles: 1, Leads: 4);
    private static readonly RowCounts Empty = new(Claims: 0, Subscriptions: 0, Profiles: 0, Leads: 0);

    private static object DefaultBody() => new { subjects = new { owner = OwnerSub, moderator = ModeratorSub } };

    private static HttpRequestMessage SeedRequest(string? key, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SeedUrl)
        {
            Content = JsonTestHelpers.JsonBody(body ?? DefaultBody()),
        };

        if (key is not null)
        {
            request.Headers.Add(DevSeedGate.HeaderName, key);
        }

        return request;
    }

    /// <summary>Seeds through the endpoint and asserts the call succeeded.</summary>
    private static async Task SeedOnceAsync(HttpClient client)
    {
        using var response = await client.SendAsync(SeedRequest(DevSeedApiFactory.Key));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_seed_is_off_by_default_and_answers_404_without_writing()
    {
        using var factory = new DevSeedApiFactory(enabled: null, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(SeedRequest(DevSeedApiFactory.Key));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Empty, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Enabled_without_a_configured_key_answers_404()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: null, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(SeedRequest("any-key"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Empty, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task A_wrong_key_answers_401_and_writes_nothing()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(SeedRequest("wrong-key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await JsonTestHelpers.ErrorCodeAsync(response));
        Assert.Equal(Empty, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task A_missing_key_answers_401_and_writes_nothing()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(SeedRequest(key: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(Empty, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task In_production_the_seed_is_refused_and_writes_nothing()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Production");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(SeedRequest(DevSeedApiFactory.Key));

        // The gate answers 404. The Production storage guard runs earlier in the pipeline and answers 503 on the InMemory
        // store (no persistence), so either refusal is accepted here. Nothing may be stored in both cases.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable });
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            Assert.Equal("storage_unavailable", await JsonTestHelpers.ErrorCodeAsync(response));
        }

        Assert.Equal(Empty, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task Enabled_with_the_key_seeds_the_billing_states_and_reports_the_counts()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(SeedRequest(DevSeedApiFactory.Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(Expected.Claims, json.GetProperty("claims").GetInt32());
        Assert.Equal(Expected.Subscriptions, json.GetProperty("subscriptions").GetInt32());
        Assert.Equal(Expected.Profiles, json.GetProperty("profiles").GetInt32());
        Assert.Equal(Expected.Leads, json.GetProperty("enquiries").GetInt32());
        Assert.False(json.GetProperty("alreadySeeded").GetBoolean());
        Assert.Equal(Expected, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task A_second_call_is_a_no_op_that_reports_already_seeded()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();
        await SeedOnceAsync(client);

        var before = await factory.CountRowsAsync();

        using var second = await client.SendAsync(SeedRequest(DevSeedApiFactory.Key));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var json = await JsonTestHelpers.ReadJsonAsync(second);
        Assert.True(json.GetProperty("alreadySeeded").GetBoolean());
        Assert.Equal(0, json.GetProperty("claims").GetInt32());
        Assert.Equal(0, json.GetProperty("enquiries").GetInt32());
        Assert.Equal(before, await factory.CountRowsAsync());
    }

    [Fact]
    public async Task The_sponsored_profile_is_public_and_labelled_as_sponsored_content()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();
        await SeedOnceAsync(client);

        using var response = await client.GetAsync("/api/v1/schools/demo-sp-1/premium");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.True(json.GetProperty("sponsored").GetBoolean());
        Assert.Equal("premium", json.GetProperty("tier").GetString());
        Assert.True(json.GetProperty("editorsVerified").GetBoolean());
        Assert.True(json.GetProperty("enquiriesOpen").GetBoolean());

        // ASCII stem only: the Polish label is JSON-escaped on the wire.
        var label = json.GetProperty("label").GetString() ?? string.Empty;
        Assert.Contains("sponsorowan", label, StringComparison.OrdinalIgnoreCase);

        var profile = json.GetProperty("profile");
        var headline = profile.GetProperty("headline").GetString() ?? string.Empty;
        Assert.Contains("DEMO", headline, StringComparison.Ordinal);
        Assert.Contains("sponsorowan", headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_claim_and_subscription_states_are_as_specified()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();
        await SeedOnceAsync(client);

        var owner = factory.HashFor(OwnerSub);
        var moderator = factory.HashFor(ModeratorSub);
        var claims = await factory.QueryAsync(db => db.SchoolClaims.AsNoTracking().ToListAsync());
        var subscriptions = await factory.QueryAsync(db => db.Subscriptions.AsNoTracking().ToListAsync());
        var profiles = await factory.QueryAsync(db => db.PremiumProfiles.AsNoTracking().ToListAsync());

        var approved = Assert.Single(claims, c => c.SchoolSlug == "demo-sp-1");
        Assert.Equal(owner, approved.OwnerHash);
        Assert.Equal(ClaimStatuses.Approved, approved.Status);
        Assert.Equal(moderator, approved.DecidedBy);
        Assert.NotNull(approved.DecidedAt);

        var pending = Assert.Single(claims, c => c.SchoolSlug == "demo-sp-2");
        Assert.Equal(owner, pending.OwnerHash);
        Assert.Equal(ClaimStatuses.Pending, pending.Status);
        Assert.Null(pending.DecidedAt);

        var subscription = Assert.Single(subscriptions);
        Assert.Equal(owner, subscription.OwnerHash);
        Assert.Equal(SubscriptionStatuses.Active, subscription.Status);
        Assert.True(subscription.CurrentPeriodEnd > DateTimeOffset.UtcNow);

        var profile = Assert.Single(profiles);
        Assert.False(profile.IsLocked);

        Assert.True(await factory.IsPremiumAsync("demo-sp-1", DateTimeOffset.UtcNow));
        Assert.False(await factory.IsPremiumAsync("demo-sp-2", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task The_enquiries_appear_in_the_owner_inbox_and_one_is_already_closed()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();
        await SeedOnceAsync(client);

        using var response = await client.SendAsync(
            BillingFixtures.Request(HttpMethod.Get, "/api/v1/schools/demo-sp-1/enquiries", sub: OwnerSub));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var leads = (await JsonTestHelpers.ReadJsonAsync(response)).GetProperty("leads");
        Assert.Equal(4, leads.GetArrayLength());

        var statuses = leads.EnumerateArray().Select(lead => lead.GetProperty("status").GetString()).ToList();
        Assert.Single(statuses, status => status == LeadStatuses.Read);
        var newCount = statuses.Count(status => status == LeadStatuses.New);
        Assert.Equal(3, newCount);
    }

    [Fact]
    public async Task Another_account_cannot_read_the_seeded_inbox()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();
        await SeedOnceAsync(client);

        using var response = await client.SendAsync(
            BillingFixtures.Request(HttpMethod.Get, "/api/v1/schools/demo-sp-1/enquiries", sub: "someone-else-sub"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_moderator_subject_is_optional_and_the_approval_then_has_no_decider()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(
            SeedRequest(DevSeedApiFactory.Key, new { subjects = new { owner = OwnerSub } }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claims = await factory.QueryAsync(db => db.SchoolClaims.AsNoTracking().ToListAsync());
        var approved = Assert.Single(claims, c => c.Status == ClaimStatuses.Approved);
        Assert.Null(approved.DecidedBy);
    }

    [Fact]
    public async Task A_body_without_an_owner_subject_answers_400_and_writes_nothing()
    {
        using var factory = new DevSeedApiFactory(enabled: true, key: DevSeedApiFactory.Key, environment: "Testing");
        var client = factory.CreateClient();

        using var response = await client.SendAsync(
            SeedRequest(DevSeedApiFactory.Key, new { subjects = new { moderator = ModeratorSub } }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await JsonTestHelpers.ErrorCodeAsync(response));
        Assert.Equal(Empty, await factory.CountRowsAsync());
    }
}
