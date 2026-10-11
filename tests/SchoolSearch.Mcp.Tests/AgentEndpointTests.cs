using System.Net;
using System.Text;
using System.Text.Json;
using SchoolSearch.Mcp.Agent;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// POST /agent/chat over the test server: the degraded states (no key, kill switch, budget), the request validation and
/// the response contract. The model is a scripted fake; no network is used.
/// </summary>
public sealed class AgentEndpointTests
{
    private const string ChatPath = "/agent/chat";
    private const string ValidBody = "{\"messages\":[{\"role\":\"user\",\"content\":\"Pokaż szkołę Szkoła\"}]}";

    [Fact]
    public async Task Without_a_key_the_endpoint_answers_503_and_calls_no_model()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("nie powinno się wykonać"));
        await using var host = await AgentTestHost.StartAsync(apiKey: string.Empty, llm: null, stub: stub);

        using var response = await host.Client.SendAsync(Post(ValidBody));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_unavailable", await ErrorCode(response));
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task The_kill_switch_answers_503_even_with_a_key()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("nie"));
        await using var host = await AgentTestHost.StartAsync(
            apiKey: "test-key",
            llm: llm,
            stub: stub,
            settings: new Dictionary<string, string?> { [AgentServiceCollectionExtensions.EnabledVariable] = "false" });

        using var response = await host.Client.SendAsync(Post(ValidBody));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_unavailable", await ErrorCode(response));
    }

    [Fact]
    public async Task A_successful_chat_returns_the_contract_with_steps_citations_and_label()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(
            false,
            Turns.Call("t1", "find_schools", "{\"query\":\"Szkoła\"}"),
            Turns.Text("Szkoła Podstawowa nr 1 (slug: szkola-podstawowa-1) ma wynik 61,4 przy kohorcie 54 uczniów."));
        await using var host = await AgentTestHost.StartAsync("test-key", llm, stub);

        using var response = await host.Client.SendAsync(Post(ValidBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Contains("szkola-podstawowa-1", root.GetProperty("reply").GetString());
        Assert.Equal("Wygenerowano przez AI", root.GetProperty("aiLabel").GetString());
        Assert.Equal("claude-haiku-5-5", root.GetProperty("model").GetString());
        Assert.Equal("end_turn", root.GetProperty("stopReason").GetString());
        Assert.Contains(root.GetProperty("citedSlugs").EnumerateArray(), slug => slug.GetString() == "szkola-podstawowa-1");

        var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.Equal("find_schools", step.GetProperty("tool").GetString());
        Assert.Equal("Szkoła", step.GetProperty("args").GetProperty("query").GetString());
        Assert.Equal("Pozycji: 2.", step.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task An_e_mail_address_in_the_question_is_refused_before_any_model_call()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("nie"));
        await using var host = await AgentTestHost.StartAsync("test-key", llm, stub);

        using var response = await host.Client.SendAsync(Post(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"Napisz do jan@example.com\"}]}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("personal_data_detected", await ErrorCode(response));
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task A_conversation_that_ends_with_an_assistant_turn_is_refused()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("nie"));
        await using var host = await AgentTestHost.StartAsync("test-key", llm, stub);

        using var response = await host.Client.SendAsync(Post(
            "{\"messages\":[{\"role\":\"user\",\"content\":\"Pytanie\"},{\"role\":\"assistant\",\"content\":\"Odp\"}]}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCode(response));
        Assert.Equal(0, llm.Calls);
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_a_validation_error_not_a_crash()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("nie"));
        await using var host = await AgentTestHost.StartAsync("test-key", llm, stub);

        using var response = await host.Client.SendAsync(Post("{nie json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_failed", await ErrorCode(response));
    }

    [Fact]
    public async Task The_endpoint_is_rate_limited_per_client_before_the_model_is_called()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(true, Turns.Text("Brak danych w katalogu."));
        await using var host = await AgentTestHost.StartAsync(
            "test-key",
            llm,
            stub,
            settings: new Dictionary<string, string?> { [AgentServiceCollectionExtensions.RequestsPerMinuteVariable] = "1" });

        using var first = await host.Client.SendAsync(Post(ValidBody));
        using var second = await host.Client.SendAsync(Post(ValidBody));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("rate_limited", await ErrorCode(second));
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task A_spent_daily_budget_answers_503_before_any_model_call()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("nie"));
        await using var host = await AgentTestHost.StartAsync(
            "test-key",
            llm,
            stub,
            settings: new Dictionary<string, string?> { [AgentServiceCollectionExtensions.DailyBudgetVariable] = "0" });

        using var response = await host.Client.SendAsync(Post(ValidBody));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_budget_exhausted", await ErrorCode(response));
        Assert.Equal(0, llm.Calls);
    }

    private static HttpRequestMessage Post(string json) =>
        new(HttpMethod.Post, ChatPath) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<string> ErrorCode(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetProperty("code").GetString() ?? string.Empty;
    }
}
