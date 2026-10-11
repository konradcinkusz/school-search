using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Models;
using static SchoolSearch.Billing.Tests.BillingFixtures;
using static SchoolSearch.Billing.Tests.JsonTestHelpers;

namespace SchoolSearch.Billing.Tests;

public sealed class PremiumEndpointTests
{
    private const string Slug = DefaultSlug;

    [Fact]
    public async Task Anonymous_read_of_a_school_without_premium_shows_no_profile_and_the_label()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/schools/{Slug}/premium");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.True(root.GetProperty("sponsored").GetBoolean());
        Assert.Equal("none", root.GetProperty("tier").GetString());
        Assert.False(root.GetProperty("editorsVerified").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("profile").ValueKind);
        Assert.Equal(PremiumCopy.SponsoredLabel, root.GetProperty("label").GetString());
        Assert.False(root.GetProperty("enquiriesOpen").GetBoolean());
    }

    [Fact]
    public async Task A_premium_school_shows_its_profile_and_the_editors_statement()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner), ProfileOf(Slug));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/schools/{Slug}/premium");

        var root = await ReadJsonAsync(response);
        Assert.Equal("premium", root.GetProperty("tier").GetString());
        Assert.True(root.GetProperty("editorsVerified").GetBoolean());
        var profile = root.GetProperty("profile");
        Assert.Equal("Szkoła z klasami sportowymi", profile.GetProperty("headline").GetString());
        Assert.Equal("https://szkola.example.pl", profile.GetProperty("websiteUrl").GetString());
        Assert.True(root.GetProperty("enquiriesOpen").GetBoolean());
    }

    [Fact]
    public async Task A_locked_profile_is_hidden_from_the_public_envelope()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner), ProfileOf(Slug, locked: true));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/schools/{Slug}/premium");

        var root = await ReadJsonAsync(response);
        Assert.Equal("none", root.GetProperty("tier").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("profile").ValueKind);
        Assert.False(root.GetProperty("enquiriesOpen").GetBoolean());
    }

    [Fact]
    public async Task Put_without_a_token_answers_401()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", content: JsonBody(ValidProfileBody()));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Put_with_an_unverified_e_mail_answers_403()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner, verified: false, content: JsonBody(ValidProfileBody()));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_not_verified", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Put_without_an_approved_claim_answers_403()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner, ClaimStatuses.Pending), SubscriptionOf(Slug, owner));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner, content: JsonBody(ValidProfileBody()));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("claim_not_approved", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Put_without_a_live_subscription_answers_403()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner, SubscriptionStatuses.Expired));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner, content: JsonBody(ValidProfileBody()));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("subscription_inactive", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Put_with_a_forbidden_term_answers_400_and_names_the_term()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner,
            content: JsonBody(ValidProfileBody("Najlepsza szkoła w mieście")));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("claim_not_allowed", await ErrorCodeAsync(response));
        var message = (await ReadJsonAsync(response)).GetProperty("error").GetProperty("message").GetString();
        Assert.Contains("Najlepsza", message!);
    }

    [Fact]
    public async Task Put_rejects_a_website_that_is_not_https()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner));
        using var client = factory.CreateClient();
        var body = new { headline = "Szkoła", description = "Opis szkoły, który jest wystarczająco długi.", websiteUrl = "http://szkola.example.pl" };
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner, content: JsonBody(body));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Put_rejects_markup_in_the_description()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner));
        using var client = factory.CreateClient();
        var body = new { headline = "Szkoła", description = "<b>Tekst</b> opisu szkoły.", websiteUrl = (string?)null };
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner, content: JsonBody(body));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task An_approved_owner_with_a_live_subscription_saves_and_publishes_the_profile()
    {
        using var factory = new BillingApiFactory();
        var owner = factory.HashFor(Owner);
        await factory.SeedAsync(ClaimOf(Slug, owner), SubscriptionOf(Slug, owner));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Put, $"/api/v1/schools/{Slug}/premium", Owner,
            content: JsonBody(ValidProfileBody("Nowy nagłówek szkoły")));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.Equal("premium", root.GetProperty("tier").GetString());
        Assert.Equal("Nowy nagłówek szkoły", root.GetProperty("profile").GetProperty("headline").GetString());

        var stored = await factory.QueryAsync(db => db.PremiumProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.SchoolSlug == Slug));
        Assert.NotNull(stored);
        Assert.Equal("Nowy nagłówek szkoły", stored!.Headline);
        Assert.False(stored.IsLocked);
    }
}
