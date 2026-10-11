using System.Net;
using System.Text;
using System.Text.Json;
using SchoolSearch.Api.Ai;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// POST /api/v1/ai/methodology-answer against a fake model. Each test asserts the HTTP answer and whether the model was called.
/// These tests need MapMethodologyQaEndpoints and AddMethodologyQa wired in Program.cs.
/// </summary>
public sealed class MethodologyQaEndpointTests
{
    private const string Url = "/api/v1/ai/methodology-answer";

    [Fact]
    public async Task Grounded_answer_is_returned_with_label_citations_model_and_grounded_flag()
    {
        var client = new FakeMethodologyQaClient();
        using var factory = new MethodologyQaApiFactory(client);

        var response = await factory.CreateClient().PostAsync(Url, Body("Dlaczego wynik ma przedział?"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("Wygenerowano przez AI", root.GetProperty("aiLabel").GetString());
        Assert.Equal("claude-haiku-5-5", root.GetProperty("model").GetString());
        Assert.True(root.GetProperty("groundedInDocument").GetBoolean());
        Assert.Equal("4.2 Coverage and the blend", root.GetProperty("citedSections")[0].GetString());
        Assert.Contains("udział zdających", root.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Without_a_key_the_endpoint_answers_503_and_never_calls_the_model()
    {
        var client = new FakeMethodologyQaClient();
        using var factory = new MethodologyQaApiFactory(client, withKey: false);

        var response = await factory.CreateClient().PostAsync(Url, Body("Dlaczego wynik ma przedział?"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_unavailable", ErrorCode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task With_the_kill_switch_off_the_endpoint_answers_503_even_with_a_key()
    {
        var client = new FakeMethodologyQaClient();
        using var factory = new MethodologyQaApiFactory(client, settings: new Dictionary<string, string>
        {
            ["AI_ENABLED"] = "false",
        });

        var response = await factory.CreateClient().PostAsync(Url, Body("Dlaczego wynik ma przedział?"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Empty_question_answers_400_and_never_calls_the_model()
    {
        var client = new FakeMethodologyQaClient();
        using var factory = new MethodologyQaApiFactory(client);

        var response = await factory.CreateClient().PostAsync(Url, Body("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", ErrorCode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Question_with_an_e_mail_address_answers_400_and_never_calls_the_model()
    {
        var client = new FakeMethodologyQaClient();
        using var factory = new MethodologyQaApiFactory(client);

        var response = await factory.CreateClient().PostAsync(Url, Body("Napisz na jan@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Hallucinated_citation_answers_502_and_the_invented_text_is_not_shown()
    {
        var client = new FakeMethodologyQaClient
        {
            Respond = (_, _) => new MethodologyQaCompletion(
                "{\"answer\":\"Zgodnie z Rozdziałem 9 tak jest.\",\"covered\":true,\"citedSections\":[\"Rozdział 9\"]}",
                100,
                40),
        };
        using var factory = new MethodologyQaApiFactory(client);

        var response = await factory.CreateClient().PostAsync(Url, Body("Dlaczego wynik ma przedział?"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ai_output_rejected", ErrorCode(body));
        Assert.DoesNotContain("Rozdział 9", body, StringComparison.Ordinal);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Per_client_allowance_answers_429_with_retry_after_for_the_call_beyond_it()
    {
        var client = new FakeMethodologyQaClient();
        using var factory = new MethodologyQaApiFactory(client, settings: new Dictionary<string, string>
        {
            ["SchoolSearch:Ai:MethodologyQa:AnswersPerIpPerHour"] = "1",
        });
        var http = factory.CreateClient();

        var first = await http.PostAsync(Url, Body("Pierwsze pytanie o próg?"));
        var second = await http.PostAsync(Url, Body("Drugie pytanie o kohortę?"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("rate_limited", ErrorCode(await second.Content.ReadAsStringAsync()));
        Assert.True(second.Headers.Contains("Retry-After"));
        Assert.Equal(1, client.CallCount);
    }

    private static StringContent Body(string question) =>
        new(JsonSerializer.Serialize(new { question }), Encoding.UTF8, "application/json");

    private static string ErrorCode(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("error").GetProperty("code").GetString()
            ?? throw new InvalidOperationException("Error body has no code: " + body);
    }
}
