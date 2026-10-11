using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolSearch.Reviews.Tests;

public sealed class DsaLegalTests : IClassFixture<DsaTestFactory>
{
    private readonly DsaTestFactory _factory;

    public DsaLegalTests(DsaTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Terms_are_published_as_a_draft_with_their_version()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/legal/terms");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(LegalDocuments.TermsVersion, doc!.RootElement.GetProperty("version").GetString());
        Assert.Equal(LegalDocuments.TermsStatus, doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Contact_point_is_returned_when_configured()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/legal/contact");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal(DsaTestFactory.ContactEmail, doc!.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Transparency_report_counts_notices_and_decisions_without_personal_data()
    {
        var before = await ReadReportAsync();

        var review = DsaTestFactory.NewReview("szkola-prawne-1", "author-l1");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });
        var notice = await _factory.CreateClient().PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            reviewId = review.Id,
            explanation = "Opinia zawiera dane osobowe ucznia, których nie należy publikować.",
            category = "personal_data",
            goodFaithConfirmed = true,
        }));
        using var receipt = await notice.Content.ReadFromJsonAsync<JsonDocument>();
        var noticeId = receipt!.RootElement.GetProperty("id").GetGuid();
        await _factory.CreateAs("mod-l1", true, "Admin").PostAsync(
            $"/api/v1/moderation/notices/{noticeId}/decision",
            DsaTestFactory.JsonBody(new { action = "keep", legalOrTermsGround = "Art. 16 DSA, pkt 2.", facts = "Brak naruszenia po sprawdzeniu." }));

        var after = await ReadReportAsync();

        Assert.Equal(before.GetProperty("notices").GetProperty("received").GetInt32() + 1, after.GetProperty("notices").GetProperty("received").GetInt32());
        Assert.Equal(before.GetProperty("notices").GetProperty("decided").GetInt32() + 1, after.GetProperty("notices").GetProperty("decided").GetInt32());
        Assert.Equal(before.GetProperty("decisions").GetProperty("total").GetInt32() + 1, after.GetProperty("decisions").GetProperty("total").GetInt32());
        Assert.Equal(0, after.GetProperty("decisions").GetProperty("automated").GetInt32());
    }

    private async Task<JsonElement> ReadReportAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/legal/transparency");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.Clone();
    }
}

public sealed class DsaLegalWithoutContactTests : IClassFixture<DsaTestFactoryNoContact>
{
    private readonly DsaTestFactoryNoContact _factory;

    public DsaLegalWithoutContactTests(DsaTestFactoryNoContact factory) => _factory = factory;

    [Fact]
    public async Task Contact_is_unavailable_and_never_invented_when_not_configured()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/legal/contact");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("contact_not_configured", doc!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }
}
