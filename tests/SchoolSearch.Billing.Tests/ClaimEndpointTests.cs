using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;
using static SchoolSearch.Billing.Tests.BillingFixtures;
using static SchoolSearch.Billing.Tests.JsonTestHelpers;

namespace SchoolSearch.Billing.Tests;

public sealed class ClaimEndpointTests
{
    private const string Slug = DefaultSlug;

    [Fact]
    public async Task A_verified_user_claims_a_school_and_the_claim_is_stored_under_a_hashed_owner()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/claims", Owner,
            content: JsonBody(new { roleDescription = "Dyrekcja" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.Equal("pending", root.GetProperty("status").GetString());

        var stored = await factory.QueryAsync(db => db.SchoolClaims.AsNoTracking()
            .FirstOrDefaultAsync(c => c.SchoolSlug == Slug));
        Assert.NotNull(stored);
        Assert.Equal(factory.HashFor(Owner), stored!.OwnerHash);
        Assert.Equal(ClaimStatuses.Pending, stored.Status);
    }

    [Fact]
    public async Task A_second_claim_for_the_same_school_answers_409()
    {
        using var factory = new BillingApiFactory();
        await factory.SeedAsync(ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Pending));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/claims", Owner,
            content: JsonBody(new { roleDescription = "Dyrekcja" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("claim_exists", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_claim_needs_a_verified_e_mail()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/claims", Owner, verified: false,
            content: JsonBody(new { roleDescription = "Dyrekcja" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_not_verified", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_blank_role_is_refused()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/claims", Owner,
            content: JsonBody(new { roleDescription = "   " }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_rejected_claim_is_reopened_in_place()
    {
        using var factory = new BillingApiFactory();
        var rejected = ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Rejected);
        await factory.SeedAsync(rejected);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/claims", Owner,
            content: JsonBody(new { roleDescription = "Wicedyrektor" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.Equal(rejected.Id, root.GetProperty("id").GetGuid());
        Assert.Equal("pending", root.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_revoked_claim_cannot_be_reopened()
    {
        using var factory = new BillingApiFactory();
        await factory.SeedAsync(ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Revoked));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/claims", Owner,
            content: JsonBody(new { roleDescription = "Dyrekcja" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("claim_revoked", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task My_claims_lists_only_the_callers_claims()
    {
        using var factory = new BillingApiFactory();
        await factory.SeedAsync(
            ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Pending),
            ClaimOf("inna-szkola", factory.HashFor("someone-else"), ClaimStatuses.Approved));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/claims/mine", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claims = (await ReadJsonAsync(response)).GetProperty("claims");
        Assert.Equal(1, claims.GetArrayLength());
        Assert.Equal(Slug, claims[0].GetProperty("schoolSlug").GetString());
        Assert.False(claims[0].TryGetProperty("notes", out _));
    }

    [Fact]
    public async Task The_moderation_queue_is_closed_to_ordinary_users()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/moderation/claims", Owner);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_moderator_lists_claims_by_status()
    {
        using var factory = new BillingApiFactory();
        await factory.SeedAsync(ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Pending));
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/moderation/claims?status=pending", Moderator, role: "Admin");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("pending", items[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task An_unknown_status_filter_is_refused()
    {
        using var factory = new BillingApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/api/v1/moderation/claims?status=wszystkie", Moderator, role: "Admin");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task A_moderator_approval_is_recorded_with_the_moderators_hash_and_a_note()
    {
        using var factory = new BillingApiFactory();
        var claim = ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Pending);
        await factory.SeedAsync(claim);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/moderation/claims/{claim.Id}/decision", Moderator, role: "Admin",
            content: JsonBody(new { action = "approve", notes = "Sprawdzono telefonicznie" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("approved", (await ReadJsonAsync(response)).GetProperty("status").GetString());

        var stored = await factory.QueryAsync(db => db.SchoolClaims.AsNoTracking().FirstAsync(c => c.Id == claim.Id));
        Assert.Equal(ClaimStatuses.Approved, stored.Status);
        Assert.Equal(factory.HashFor(Moderator), stored.DecidedBy);
        Assert.Equal("Sprawdzono telefonicznie", stored.Notes);
        Assert.True(stored.DecidedAt.HasValue);
    }

    [Fact]
    public async Task A_decision_that_breaks_the_transition_rules_is_refused()
    {
        using var factory = new BillingApiFactory();
        var claim = ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Approved);
        await factory.SeedAsync(claim);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/moderation/claims/{claim.Id}/decision", Moderator, role: "Admin",
            content: JsonBody(new { action = "approve" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("invalid_transition", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task An_unknown_moderator_action_is_refused()
    {
        using var factory = new BillingApiFactory();
        var claim = ClaimOf(Slug, factory.HashFor(Owner), ClaimStatuses.Pending);
        await factory.SeedAsync(claim);
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/moderation/claims/{claim.Id}/decision", Moderator, role: "Admin",
            content: JsonBody(new { action = "usun" }));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCodeAsync(response));
    }
}
