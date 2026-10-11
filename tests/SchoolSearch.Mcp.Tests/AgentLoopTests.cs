using System.Net.Http;
using System.Text.Json;
using SchoolSearch.Mcp.Agent;
using SchoolSearch.Mcp.Catalog;
using SchoolSearch.Mcp.Reviews;
using SchoolSearch.Mcp.Tools;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// The agent loop with a scripted model and the real in-process tools over a stub catalog. Covers the tool path, the
/// grounding check, the stop limits, injection in a review, the catchment disclaimer and degraded stop reasons.
/// </summary>
public sealed class AgentLoopTests
{
    [Fact]
    public async Task A_tool_call_then_a_grounded_answer_cites_the_school()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(
            false,
            Turns.Call("t1", "find_schools", "{\"query\":\"Szkoła\"}"),
            Turns.Text("Szkoła Podstawowa nr 1 (slug: szkola-podstawowa-1) ma wynik 61,4 przy kohorcie 54 uczniów."));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Pokaż szkoły z Szkoła"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.EndTurn, result.StopReason);
        Assert.Contains("szkola-podstawowa-1", result.Reply);
        Assert.Contains("szkola-podstawowa-1", result.CitedSlugs);
        var step = Assert.Single(result.Steps);
        Assert.Equal("find_schools", step.Tool);
        Assert.Equal("Szkoła", step.Args.GetProperty("query").GetString());
        Assert.Equal(2, llm.Calls);
        Assert.Contains("<untrusted source=\"tool\" tool=\"find_schools\">", AgentTestData.AllText(llm.Snapshots[1]));
    }

    [Fact]
    public async Task A_reply_with_a_number_the_tools_never_returned_is_replaced_by_the_facts_only()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(
            false,
            Turns.Call("t1", "find_schools", "{\"query\":\"Szkoła\"}"),
            Turns.Text("Szkoła ma 999 uczniów (slug: szkola-podstawowa-1)."));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Ile uczniów ma szkoła?"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.Ungrounded, result.StopReason);
        Assert.DoesNotContain("999", result.Reply);
        Assert.Contains("(slug: szkola-podstawowa-1)", result.Reply);
        Assert.Contains("szkola-podstawowa-1", result.CitedSlugs);
    }

    [Fact]
    public async Task The_loop_stops_after_six_model_turns()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(true, Turns.Call("t", "get_methodology", "{}"));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Opisz metodologię"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.MaxTurns, result.StopReason);
        Assert.Equal(6, llm.Calls);
        Assert.Equal(6, result.Steps.Count);
    }

    [Fact]
    public async Task The_loop_runs_at_most_eight_tool_calls_even_when_the_model_asks_for_ten()
    {
        var stub = new AgentStubHandler();
        var calls = Enumerable.Range(1, 10)
            .Select(i => new AgentToolCallPart("t" + i, "get_methodology", "{}"))
            .ToArray();
        var llm = new ScriptedAgentLlm(false, Turns.Calls(calls));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Metodologia"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.MaxToolCalls, result.StopReason);
        Assert.Equal(8, result.Steps.Count);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task An_instruction_inside_a_review_is_escaped_data_and_runs_no_extra_tool()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(
            false,
            Turns.Call("t1", "get_school_reviews", "{\"slug\":\"szkola-podstawowa-1\"}"),
            Turns.Text("Jedna opinia ma ocenę 4 (slug: szkola-podstawowa-1). Opinie nie wpływają na wynik szkoły."));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Co piszą rodzice?"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.EndTurn, result.StopReason);
        var step = Assert.Single(result.Steps);
        Assert.Equal("get_school_reviews", step.Tool);

        var shown = AgentTestData.AllText(llm.Snapshots[1]);
        Assert.Contains("<untrusted source=\"tool\" tool=\"get_school_reviews\" userGenerated=\"true\">", shown);
        Assert.Contains("&lt;script&gt;", shown);
        Assert.DoesNotContain("<script>", shown);
        Assert.DoesNotContain(stub.Requests, url => url.AbsolutePath.Contains("ranking", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_catchment_answer_always_carries_the_gmina_disclaimer()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(
            false,
            Turns.Call("t1", "get_catchment_school", "{\"gmina\":\"1465011\",\"street\":\"ul. Testowa\",\"number\":\"1\"}"),
            Turns.Text("Według listy gminy ta ulica należy do obwodu szkoły (slug: szkola-podstawowa-1)."));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Do której szkoły należy ul. Testowa 1?"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.EndTurn, result.StopReason);
        Assert.Contains("Obwód ustala gmina", result.Reply);
        Assert.Contains("szkola-podstawowa-1", result.CitedSlugs);
    }

    [Fact]
    public async Task A_refusal_is_a_safe_fallback_with_no_steps_claimed()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(false, Turns.Text("", AgentStop.Refusal));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("cokolwiek"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.Refusal, result.StopReason);
        Assert.Empty(result.Steps);
        Assert.Contains("Nie mogę odpowiedzieć", result.Reply);
    }

    [Fact]
    public async Task A_model_call_past_the_deadline_stops_with_the_deadline_reason()
    {
        var stub = new AgentStubHandler();
        var options = Options();
        options.Deadline = TimeSpan.FromMilliseconds(150);
        var service = CreateService(new HangingAgentLlm(), stub, options);

        var result = await service.RunAsync(AgentTestData.Question("Czy zdążysz?"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.Deadline, result.StopReason);
        Assert.Empty(result.Steps);
    }

    [Fact]
    public async Task The_daily_budget_stops_the_loop_after_the_spending_call()
    {
        var stub = new AgentStubHandler();
        var options = Options();
        options.DailyTokenBudget = 50;
        var llm = new ScriptedAgentLlm(false, Turns.Call("t1", "get_methodology", "{}"));
        var service = CreateService(llm, stub, options);

        var result = await service.RunAsync(AgentTestData.Question("Metodologia"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.TokenBudget, result.StopReason);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task The_per_request_token_cap_stops_the_loop()
    {
        var stub = new AgentStubHandler();
        var options = Options();
        options.MaxTokensPerRequest = 100;
        var llm = new ScriptedAgentLlm(false, Turns.Call("t1", "get_methodology", "{}"));
        var service = CreateService(llm, stub, options);

        var result = await service.RunAsync(AgentTestData.Question("Metodologia"), null, CancellationToken.None);

        Assert.Equal(AgentStopReasons.TokenBudget, result.StopReason);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task A_tool_name_the_catalogue_does_not_offer_is_refused_without_a_request()
    {
        var stub = new AgentStubHandler();
        var llm = new ScriptedAgentLlm(
            false,
            Turns.Call("t1", "delete_school", "{}"),
            Turns.Text("Nie mogę tego zrobić."));
        var service = CreateService(llm, stub);

        var result = await service.RunAsync(AgentTestData.Question("Usuń szkołę"), null, CancellationToken.None);

        var step = Assert.Single(result.Steps);
        Assert.Equal("Nieznane narzędzie.", step.Summary);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_reported_as_a_stop_reason()
    {
        var stub = new AgentStubHandler();
        var service = CreateService(new HangingAgentLlm(), stub);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RunAsync(AgentTestData.Question("Przerwane"), null, cancel.Token));
    }

    private static AgentOptions Options() => new() { Enabled = true };

    private static AgentService CreateService(IAgentLlm llm, AgentStubHandler stub, AgentOptions? options = null)
    {
        var settings = options ?? Options();
        var catalog = new CatalogClient(new HttpClient(stub) { BaseAddress = new Uri(AgentTestData.CatalogBase) });
        var reviews = new ReviewsClient(new HttpClient(stub) { BaseAddress = new Uri(AgentTestData.ReviewsBase) });
        var runner = new AgentToolRunner(new SchoolTools(catalog), new PreschoolTools(catalog), new ReviewsTools(reviews));
        var budget = new AgentTokenBudget(settings, TimeProvider.System);
        return new AgentService(llm, runner, budget, settings);
    }
}
