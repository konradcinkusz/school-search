using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolSearch.Reviews.Tests;

public sealed class DsaNoticeTests : IClassFixture<DsaTestFactory>
{
    private const string Explanation = "Treść opinii zawiera dane osobowe nauczyciela, co narusza regulamin.";

    private readonly DsaTestFactory _factory;

    public DsaNoticeTests(DsaTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Notice_without_good_faith_confirmation_is_refused()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            contentReference = "https://example.test/opinia/1",
            explanation = Explanation,
            category = "personal_data",
            goodFaithConfirmed = false,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("validation_failed", doc!.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Notice_is_accepted_with_an_ack_token_that_unlocks_its_status()
    {
        var client = _factory.CreateClient();

        var created = await client.PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            contentReference = "https://example.test/opinia/2",
            explanation = Explanation,
            category = "personal_data",
            goodFaithConfirmed = true,
        }));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>();
        var id = receipt!.RootElement.GetProperty("id").GetGuid();
        var ackToken = receipt.RootElement.GetProperty("ackToken").GetString()!;
        Assert.Equal(22, ackToken.Length);

        var status = await client.GetAsync($"/api/v1/notices/{id}?token={ackToken}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var body = await status.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("received", body!.RootElement.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("decision").ValueKind);
    }

    [Fact]
    public async Task Notice_status_is_not_visible_without_the_right_token()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            contentReference = "https://example.test/opinia/3",
            explanation = Explanation,
            category = "defamation",
            goodFaithConfirmed = true,
        }));
        using var receipt = await created.Content.ReadFromJsonAsync<JsonDocument>();
        var id = receipt!.RootElement.GetProperty("id").GetGuid();

        var withoutToken = await client.GetAsync($"/api/v1/notices/{id}");
        var wrongToken = await client.GetAsync($"/api/v1/notices/{id}?token={new string('A', 22)}");

        Assert.Equal(HttpStatusCode.NotFound, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
    }

    [Fact]
    public async Task Notice_for_an_unknown_review_is_not_found()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            reviewId = Guid.NewGuid(),
            explanation = Explanation,
            category = "other",
            goodFaithConfirmed = true,
        }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Notice_needs_a_content_reference_or_a_review()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            explanation = Explanation,
            category = "other",
            goodFaithConfirmed = true,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
