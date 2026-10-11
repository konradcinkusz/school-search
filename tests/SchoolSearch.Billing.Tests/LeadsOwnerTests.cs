using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolSearch.Billing.Leads;

namespace SchoolSearch.Billing.Tests;

public sealed class LeadsOwnerTests : IClassFixture<LeadsApiFactory>
{
    private readonly LeadsApiFactory _factory;

    public LeadsOwnerTests(LeadsApiFactory factory) => _factory = factory;

    private static string NewSlug() => "szkola-" + Guid.NewGuid().ToString("N");

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [Fact]
    public async Task Owner_lists_enquiries_of_their_school()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-list", Now.AddMonths(1));
        var leadId = await _factory.SeedLeadAsync(slug, Now.AddMinutes(-5), Now.AddDays(90));
        var client = LeadsTestClient.Create(_factory, "198.51.100.20");

        var response = await LeadsTestClient.SendAsync(client, HttpMethod.Get, $"/api/v1/schools/{slug}/enquiries", sub: "owner-list");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var leads = body!.RootElement.GetProperty("leads").EnumerateArray().ToList();
        var lead = Assert.Single(leads);
        Assert.Equal(leadId, lead.GetProperty("id").GetGuid());
        Assert.Equal("new", lead.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_user_cannot_read_the_enquiries_of_a_school_they_do_not_represent()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-private", Now.AddMonths(1));
        await _factory.SeedLeadAsync(slug, Now, Now.AddDays(90));
        var client = LeadsTestClient.Create(_factory, "198.51.100.21");

        var response = await LeadsTestClient.SendAsync(client, HttpMethod.Get, $"/api/v1/schools/{slug}/enquiries", sub: "stranger-42");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_caller_cannot_read_enquiries()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-anon", Now.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.22");

        var response = await LeadsTestClient.SendAsync(client, HttpMethod.Get, $"/api/v1/schools/{slug}/enquiries");

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Unverified_owner_cannot_read_enquiries()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-unverified", Now.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.23");

        var response = await LeadsTestClient.SendAsync(
            client, HttpMethod.Get, $"/api/v1/schools/{slug}/enquiries", sub: "owner-unverified", emailVerified: false);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task Owner_listing_with_markRead_marks_unread_enquiries_after_the_response()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-read", Now.AddMonths(1));
        var leadId = await _factory.SeedLeadAsync(slug, Now.AddMinutes(-1), Now.AddDays(90));
        var client = LeadsTestClient.Create(_factory, "198.51.100.24");

        var first = await LeadsTestClient.SendAsync(client, HttpMethod.Get, $"/api/v1/schools/{slug}/enquiries?markRead=true", sub: "owner-read");
        using var firstBody = await first.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("new", firstBody!.RootElement.GetProperty("leads")[0].GetProperty("status").GetString());

        var stored = await _factory.FindLeadAsync(leadId);
        Assert.Equal(LeadRules.StatusRead, stored!.Status);
    }

    [Fact]
    public async Task Owner_can_delete_an_enquiry_and_it_is_gone()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-delete", Now.AddMonths(1));
        var leadId = await _factory.SeedLeadAsync(slug, Now, Now.AddDays(90));
        var client = LeadsTestClient.Create(_factory, "198.51.100.25");

        var response = await LeadsTestClient.SendAsync(client, HttpMethod.Delete, $"/api/v1/enquiries/{leadId}", sub: "owner-delete");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await _factory.FindLeadAsync(leadId));
    }

    [Fact]
    public async Task Deleting_another_schools_enquiry_answers_not_found_and_keeps_it()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-victim", Now.AddMonths(1));
        var leadId = await _factory.SeedLeadAsync(slug, Now, Now.AddDays(90));
        var client = LeadsTestClient.Create(_factory, "198.51.100.26");

        var response = await LeadsTestClient.SendAsync(client, HttpMethod.Delete, $"/api/v1/enquiries/{leadId}", sub: "attacker-7");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(await _factory.FindLeadAsync(leadId));
    }
}
