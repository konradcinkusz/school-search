using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SchoolSearch.Billing.Leads;

namespace SchoolSearch.Billing.Tests;

public sealed class LeadsEnquiryTests : IClassFixture<LeadsApiFactory>
{
    private const string ValidMessage = "Czy szkoła prowadzi zajęcia dodatkowe z języka angielskiego?";
    private const string ValidEmail = "rodzic@example.test";

    private readonly LeadsApiFactory _factory;

    public LeadsEnquiryTests(LeadsApiFactory factory) => _factory = factory;

    private static object Enquiry(string email = ValidEmail, string message = ValidMessage, string? consent = null, string website = "") => new
    {
        email,
        message,
        acceptedConsentVersion = consent ?? LeadConsent.Version,
        website,
    };

    private static string NewSlug() => "szkola-" + Guid.NewGuid().ToString("N");

    private Task<HttpResponseMessage> PostAsync(HttpClient client, string slug, object body) =>
        LeadsTestClient.SendAsync(client, HttpMethod.Post, $"/api/v1/schools/{slug}/enquiries", body: body);

    [Fact]
    public async Task Enquiry_to_an_open_premium_school_is_accepted_and_stored_with_consent_and_expiry()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-accept", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.1");

        var response = await PostAsync(client, slug, Enquiry());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var lead = Assert.Single(await _factory.ListLeadsAsync(slug));
        Assert.Equal(ValidEmail, lead.ParentEmail);
        Assert.Equal(ValidMessage, lead.Message);
        Assert.Equal(LeadConsent.Version, lead.ConsentVersion);
        Assert.Equal(LeadRules.StatusNew, lead.Status);
        Assert.Equal(lead.CreatedAt.AddDays(LeadRules.RetentionDays), lead.ExpiresAt);
    }

    [Fact]
    public async Task Receipt_carries_no_identifier_so_the_caller_cannot_probe_storage()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-receipt", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.2");

        var response = await PostAsync(client, slug, Enquiry());

        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("received", body!.RootElement.GetProperty("status").GetString());
        Assert.False(body.RootElement.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task Enquiry_to_a_school_without_a_premium_profile_is_not_found_and_stores_nothing()
    {
        var slug = NewSlug();
        var client = LeadsTestClient.Create(_factory, "198.51.100.3");

        var response = await PostAsync(client, slug, Enquiry());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await _factory.ListLeadsAsync(slug));
    }

    [Fact]
    public async Task Enquiry_to_a_school_with_a_locked_profile_is_not_found()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-locked", DateTimeOffset.UtcNow.AddMonths(1), profileLocked: true);
        var client = LeadsTestClient.Create(_factory, "198.51.100.4");

        var response = await PostAsync(client, slug, Enquiry());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await _factory.ListLeadsAsync(slug));
    }

    [Fact]
    public async Task Enquiry_to_a_school_whose_subscription_has_expired_is_not_found()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-expired", DateTimeOffset.UtcNow.AddDays(-1), subscriptionStatus: "active");
        var client = LeadsTestClient.Create(_factory, "198.51.100.5");

        var response = await PostAsync(client, slug, Enquiry());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Enquiry_is_still_accepted_while_the_subscription_is_canceling_before_period_end()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-canceling", DateTimeOffset.UtcNow.AddDays(10), subscriptionStatus: "canceling");
        var client = LeadsTestClient.Create(_factory, "198.51.100.6");

        var response = await PostAsync(client, slug, Enquiry());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task Enquiry_without_the_current_consent_version_is_refused()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-consent", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.7");

        var response = await PostAsync(client, slug, Enquiry(consent: "some-old-version"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("consent_required", body!.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(await _factory.ListLeadsAsync(slug));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two@@example.test")]
    [InlineData("   ")]
    public async Task Enquiry_with_an_invalid_e_mail_is_refused(string email)
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-email", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.8");

        var response = await PostAsync(client, slug, Enquiry(email: email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await _factory.ListLeadsAsync(slug));
    }

    [Fact]
    public async Task Enquiry_message_length_bounds_are_enforced()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-length", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.9");

        var tooShort = await PostAsync(client, slug, Enquiry(message: new string('a', LeadRules.MinMessageLength - 1)));
        var tooLong = await PostAsync(client, slug, Enquiry(message: new string('a', LeadRules.MaxMessageLength + 1)));
        var atMinimum = await PostAsync(client, slug, Enquiry(message: new string('a', LeadRules.MinMessageLength)));

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, atMinimum.StatusCode);
    }

    [Fact]
    public async Task Honeypot_filled_enquiry_gets_the_same_202_and_stores_nothing()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-honeypot", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.10");

        var response = await PostAsync(client, slug, Enquiry(website: "https://spam.example.test"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await _factory.ListLeadsAsync(slug));
    }

    [Fact]
    public async Task Enquiries_are_rate_limited_per_client_ip()
    {
        var slug = NewSlug();
        await _factory.SeedPremiumSchoolAsync(slug, "owner-rate", DateTimeOffset.UtcNow.AddMonths(1));
        var client = LeadsTestClient.Create(_factory, "198.51.100.50");

        for (var i = 0; i < 5; i++)
        {
            var accepted = await PostAsync(client, slug, Enquiry());
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        var limited = await PostAsync(client, slug, Enquiry());

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(5, (await _factory.ListLeadsAsync(slug)).Count);
    }

    [Fact]
    public async Task Malformed_json_is_refused_without_echoing_the_body()
    {
        var slug = NewSlug();
        var client = LeadsTestClient.Create(_factory, "198.51.100.11");
        var content = new StringContent("{\"email\": \"rodzic@example.test\", \"message\": ", Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"/api/v1/schools/{slug}/enquiries", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("rodzic@example.test", text);
    }

    [Fact]
    public async Task Consent_endpoint_serves_the_current_version_as_a_draft()
    {
        var client = LeadsTestClient.Create(_factory, "198.51.100.12");

        var response = await LeadsTestClient.SendAsync(client, HttpMethod.Get, "/api/v1/legal/enquiry-consent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(LeadConsent.Version, body!.RootElement.GetProperty("version").GetString());
        Assert.Equal("draft", body.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("text").GetString()));
    }
}
