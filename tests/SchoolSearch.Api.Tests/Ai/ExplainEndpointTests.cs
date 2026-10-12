using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SchoolSearch.Api.Ai;
using SchoolSearch.Api.Tests.Public;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Behaviour of POST /api/v1/schools/{slug}/explain against a fake model. Every test asserts both the HTTP answer
/// and whether the model was called, because the cost guards are defined by that second fact.
/// </summary>
public sealed class ExplainEndpointTests
{
    private const string CohortPattern = "\"cohortSize\":(\\d+)";

    private static string Url(string slug) => $"/api/v1/schools/{slug}/explain";

    [Fact]
    public async Task Grounded_text_is_accepted_and_returned_with_its_label_facts_and_model()
    {
        var client = new FakeExplainClient
        {
            // The text quotes the cohort size from the facts it was given, so it must be grounded.
            Respond = (_, user) => new ExplainCompletion(
                "Wynik dotyczy egzaminu z 2025 roku, a w kohorcie było " + Regex.Match(user, CohortPattern).Groups[1].Value + " uczniów.",
                120,
                80),
        };
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(PublicSeed.SchoolA, root.GetProperty("slug").GetString());
        Assert.Equal("Wygenerowano przez AI", root.GetProperty("label").GetString());
        Assert.Equal("1.0.0", root.GetProperty("methodologyVersion").GetString());
        Assert.Equal(2025, root.GetProperty("examYear").GetInt32());
        Assert.False(root.GetProperty("cached").GetBoolean());
        Assert.Contains("w kohorcie było 10 uczniów", root.GetProperty("text").GetString());
        Assert.EndsWith(ExplainPromptBuilder.Disclaimer, root.GetProperty("text").GetString());

        var facts = root.GetProperty("facts");
        Assert.Equal("published", facts.GetProperty("scoreStatus").GetString());
        Assert.Equal(JsonValueKind.Number, facts.GetProperty("score").GetProperty("score").ValueKind);
        Assert.Equal(10, facts.GetProperty("cohortSize").GetInt32());

        Assert.Equal(1, client.CallCount);
        Assert.Contains(ExplainPromptBuilder.Disclaimer, client.Prompts[0].System);
    }

    [Fact]
    public async Task Text_with_an_invented_number_is_rejected_with_502_and_not_shown()
    {
        var client = new FakeExplainClient
        {
            Respond = (_, _) => new ExplainCompletion("Szkoła osiągnęła 98765 punktów.", 100, 50),
        };
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ai_output_rejected", ErrorCode(body));
        Assert.DoesNotContain("98765", body);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Refused_or_empty_model_output_is_rejected_with_502()
    {
        var client = new FakeExplainClient
        {
            Respond = (_, _) => new ExplainCompletion(string.Empty, 100, 0),
        };
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("ai_output_rejected", ErrorCode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Without_a_key_the_endpoint_answers_503_and_never_calls_the_model()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client, withKey: false);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_unavailable", ErrorCode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task With_the_kill_switch_off_the_endpoint_answers_503_even_with_a_key()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client, settings: new Dictionary<string, string>
        {
            ["AI_ENABLED"] = "false",
        });
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Zero_daily_budget_answers_503_budget_exhausted_without_a_model_call()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client, settings: new Dictionary<string, string>
        {
            ["AI_DAILY_TOKEN_BUDGET"] = "0",
        });
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_budget_exhausted", ErrorCode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Budget_spent_by_one_call_stops_the_next_uncached_call()
    {
        var client = new FakeExplainClient
        {
            Respond = (_, _) => new ExplainCompletion(FakeExplainClient.NeutralText, 3, 3),
        };
        using var factory = new AiApiFactory(client, settings: new Dictionary<string, string>
        {
            ["AI_DAILY_TOKEN_BUDGET"] = "5",
        });
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var http = factory.CreateClient();

        var first = await http.PostAsync(Url(PublicSeed.SchoolA), null);
        var second = await http.PostAsync(Url(PublicSeed.SchoolB), null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.Equal("ai_budget_exhausted", ErrorCode(await second.Content.ReadAsStringAsync()));
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task A_cache_hit_does_not_call_the_model_a_second_time()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var http = factory.CreateClient();

        var first = await JsonTestHelpers.ReadJsonAsync(await http.PostAsync(Url(PublicSeed.SchoolA), null));
        var second = await JsonTestHelpers.ReadJsonAsync(await http.PostAsync(Url(PublicSeed.SchoolA), null));

        Assert.False(first.GetProperty("cached").GetBoolean());
        Assert.True(second.GetProperty("cached").GetBoolean());
        Assert.Equal(first.GetProperty("text").GetString(), second.GetProperty("text").GetString());
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task Per_client_hourly_allowance_answers_429_for_the_call_beyond_it()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client, settings: new Dictionary<string, string>
        {
            ["SchoolSearch:Ai:ExplainsPerIpPerHour"] = "2",
        });
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync(Url(PublicSeed.SchoolA), null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync(Url(PublicSeed.SchoolB), null)).StatusCode);
        var limited = await http.PostAsync(Url(PublicSeed.SchoolC), null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", ErrorCode(await limited.Content.ReadAsStringAsync()));
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task Unknown_slug_answers_404_and_never_calls_the_model()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url("szkoly-nie-ma-takiej"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", ErrorCode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Malformed_slug_answers_400_validation_failed()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().PostAsync(Url("Bad_Slug"), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Language_other_than_pl_is_refused_with_400()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var body = new StringContent("{\"lang\":\"en\"}", Encoding.UTF8, "application/json");
        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task Suppressed_school_prompt_and_facts_contain_no_score_number()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        // School E has 5 pupils who sat the exam: inside the limited band, below the publication threshold, so no score.
        var response = await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolE), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal("limited", root.GetProperty("facts").GetProperty("scoreStatus").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("facts").GetProperty("score").GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("facts").GetProperty("score").GetProperty("intervalLow").ValueKind);

        var user = Assert.Single(client.Prompts).User;
        Assert.Contains("\"scoreStatus\":\"limited\"", user);
        Assert.Contains("\"score\":null", user);
        Assert.DoesNotMatch("\"score\":-?[0-9]", user);
        Assert.DoesNotMatch("\"intervalLow\":-?[0-9]", user);
        Assert.DoesNotMatch("\"intervalHigh\":-?[0-9]", user);
    }

    [Fact]
    public async Task Prompt_carries_no_school_name_or_operator_field()
    {
        var client = new FakeExplainClient();
        using var factory = new AiApiFactory(client);
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        await factory.CreateClient().PostAsync(Url(PublicSeed.SchoolA), null);

        var user = Assert.Single(client.Prompts).User;
        Assert.DoesNotContain("Szkola Podstawowa nr 1", user);
        Assert.DoesNotContain("governingBody", user);
        Assert.DoesNotContain("operator", user, StringComparison.OrdinalIgnoreCase);
    }

    private static string ErrorCode(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("error").GetProperty("code").GetString()
            ?? throw new InvalidOperationException("Error body has no code: " + body);
    }
}
