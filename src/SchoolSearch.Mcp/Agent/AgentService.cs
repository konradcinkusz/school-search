using System.Text.Json;

namespace SchoolSearch.Mcp.Agent;

/// <summary>One tool call the agent made, as the user sees it: the tool, the arguments the model sent, and a short summary.</summary>
public sealed record AgentStep(string Tool, JsonElement Args, string Summary);

/// <summary>The agent's answer: the reply text, the steps, the cited school slugs and why the loop stopped.</summary>
public sealed record AgentRunResult(string Reply, IReadOnlyList<AgentStep> Steps, IReadOnlyList<string> CitedSlugs, string StopReason);

/// <summary>Stop reasons reported in <see cref="AgentRunResult.StopReason"/>.</summary>
public static class AgentStopReasons
{
    public const string EndTurn = "end_turn";
    public const string MaxTurns = "max_turns";
    public const string MaxToolCalls = "max_tool_calls";
    public const string Deadline = "deadline";
    public const string TokenBudget = "token_budget";
    public const string Refusal = "refusal";
    public const string MaxTokens = "max_tokens";
    public const string NoAnswer = "no_answer";
    public const string Ungrounded = "ungrounded";
}

/// <summary>
/// The manual tool-use loop of "Doradca wyboru szkoły". Each model turn either ends with an answer or asks for tools,
/// which run in-process and go back to the model as untrusted data. Limits: model turns, tool calls, tokens per request,
/// a daily token budget and one overall deadline. The final answer passes the digit-grounding check against the tool
/// results; otherwise the reply is replaced by a fallback that lists only facts the tools returned.
/// </summary>
public sealed class AgentService(IAgentLlm llm, AgentToolRunner tools, AgentTokenBudget budget, AgentOptions options)
{
    /// <summary>Runs one request. <paramref name="conversation"/> holds already wrapped, validated messages.</summary>
    public async Task<AgentRunResult> RunAsync(IReadOnlyList<AgentMessage> conversation, string? gmina, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(options.Deadline);
        var linked = deadline.Token;

        var transcript = new List<AgentMessage>(conversation);
        var steps = new List<AgentStep>();
        var toolResults = new List<string>();
        var system = AgentPrompts.SystemPromptFor(gmina);
        var modelTurns = 0;
        var toolCalls = 0;
        long requestTokens = 0;
        var catchmentFound = false;
        var stop = AgentStopReasons.NoAnswer;
        var finalText = string.Empty;

        while (true)
        {
            if (modelTurns >= options.MaxModelTurns)
            {
                stop = AgentStopReasons.MaxTurns;
                break;
            }

            if (requestTokens >= options.MaxTokensPerRequest || budget.IsExhausted())
            {
                stop = AgentStopReasons.TokenBudget;
                break;
            }

            if (linked.IsCancellationRequested)
            {
                stop = AgentStopReasons.Deadline;
                break;
            }

            AgentTurn turn;
            modelTurns++;
            try
            {
                turn = await llm.NextAsync(transcript, AgentToolCatalog.Definitions, system, linked);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                stop = AgentStopReasons.Deadline;
                break;
            }

            var tokens = (long)turn.InputTokens + turn.OutputTokens;
            requestTokens += tokens;
            budget.Record(tokens);

            if (turn.Stop == AgentStop.Refusal)
            {
                stop = AgentStopReasons.Refusal;
                break;
            }

            if (turn.Stop == AgentStop.MaxTokens)
            {
                stop = AgentStopReasons.MaxTokens;
                break;
            }

            var calls = turn.Content.OfType<AgentToolCallPart>().ToList();
            if (turn.Stop == AgentStop.ToolUse && calls.Count > 0)
            {
                transcript.Add(new AgentMessage(AgentRole.Assistant, turn.Content));
                var replies = new List<AgentPart>();
                foreach (var call in calls)
                {
                    if (toolCalls >= options.MaxToolCalls)
                    {
                        stop = AgentStopReasons.MaxToolCalls;
                        break;
                    }

                    toolCalls++;
                    AgentToolOutcome outcome;
                    try
                    {
                        outcome = await tools.RunAsync(call.Name, call.ArgumentsJson, linked);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        stop = AgentStopReasons.Deadline;
                        break;
                    }

                    steps.Add(new AgentStep(call.Name, AgentToolRunner.ParseArguments(call.ArgumentsJson), outcome.Summary));
                    toolResults.Add(outcome.ContentJson);
                    if (call.Name == "get_catchment_school" && !outcome.IsError)
                    {
                        catchmentFound = true;
                    }

                    replies.Add(new AgentToolResultPart(call.Id, AgentPrompts.WrapToolResult(call.Name, outcome.ContentJson)));
                }

                if (stop is AgentStopReasons.MaxToolCalls or AgentStopReasons.Deadline)
                {
                    break;
                }

                transcript.Add(new AgentMessage(AgentRole.User, replies));
                continue;
            }

            finalText = string.Concat(turn.Content.OfType<AgentTextPart>().Select(part => part.Text)).Trim();
            stop = turn.Stop == AgentStop.EndTurn && finalText.Length > 0 ? AgentStopReasons.EndTurn : AgentStopReasons.NoAnswer;
            break;
        }

        // The caller's cancellation is not an answer: it propagates. Only the deadline is reported as a stop reason.
        ct.ThrowIfCancellationRequested();

        var finalStop = stop;
        string reply;
        if (stop == AgentStopReasons.EndTurn)
        {
            if (AgentGrounding.IsGrounded(finalText, toolResults, gmina))
            {
                reply = finalText;
            }
            else
            {
                finalStop = AgentStopReasons.Ungrounded;
                reply = AgentGrounding.Fallback(LeadFor(finalStop), toolResults);
            }
        }
        else
        {
            reply = AgentGrounding.Fallback(LeadFor(stop), toolResults);
        }

        if (catchmentFound)
        {
            reply = AgentGrounding.EnsureCatchmentDisclaimer(reply);
        }

        var cited = AgentGrounding.CitedSlugs(reply, AgentGrounding.CollectSlugs(toolResults));
        return new AgentRunResult(reply, steps, cited, finalStop);
    }

    private static string LeadFor(string stop) => stop switch
    {
        AgentStopReasons.Ungrounded => "Nie mogę podać tej odpowiedzi w sprawdzonej formie. Oto tylko dane pobrane z katalogu:",
        AgentStopReasons.Refusal => "Nie mogę odpowiedzieć na tę wiadomość. Oto dane pobrane z katalogu:",
        _ => "Nie udało się dokończyć odpowiedzi w dozwolonym limicie. Oto dane pobrane z katalogu:",
    };
}
