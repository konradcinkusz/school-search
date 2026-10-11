using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// The only file in SchoolSearch.Mcp that references the Anthropic SDK (ADR-0011, ADR-0017). It maps the SDK-agnostic
/// conversation to Messages API blocks and the answer back. Request shapes follow
/// src/SchoolSearch.Api/Ai/AnthropicExplainClient.cs and the claude-api C# tool-use reference: tools are Tool values with
/// an InputSchema, tool answers are ToolResultBlockParam values, and every block of an assistant turn is echoed back.
/// The rules are sent as the first block of the first user turn, not through the SDK's system parameter.
/// </summary>
internal sealed class AnthropicAgentLlm(AnthropicClient client, AgentOptions options) : IAgentLlm
{
    /// <summary>Builds the SDK-backed model client. Called only when a key is configured.</summary>
    internal static AnthropicAgentLlm Create(string apiKey, AgentOptions options) =>
        new(new AnthropicClient { ApiKey = apiKey }, options);

    public async Task<AgentTurn> NextAsync(
        IReadOnlyList<AgentMessage> history,
        IReadOnlyList<AgentToolDef> tools,
        string system,
        CancellationToken ct)
    {
        var sdkTools = new List<ToolUnion>(tools.Count);
        foreach (var tool in tools)
        {
            Tool sdkTool = new()
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = new()
                {
                    Properties = tool.Properties.ToDictionary(p => p.Key, p => p.Value),
                    Required = [.. tool.Required],
                },
            };
            sdkTools.Add(sdkTool);
        }

        var messages = new List<MessageParam>(history.Count);
        for (var index = 0; index < history.Count; index++)
        {
            var entry = history[index];
            var blocks = new List<ContentBlockParam>();
            if (index == 0)
            {
                blocks.Add(new TextBlockParam { Text = system });
            }

            foreach (var part in entry.Parts)
            {
                switch (part)
                {
                    case AgentTextPart text:
                        blocks.Add(new TextBlockParam { Text = text.Text });
                        break;
                    case AgentToolCallPart call:
                        blocks.Add(new ToolUseBlockParam
                        {
                            ID = call.Id,
                            Name = call.Name,
                            Input = ToInput(call.ArgumentsJson),
                        });
                        break;
                    case AgentToolResultPart result:
                        blocks.Add(new ToolResultBlockParam
                        {
                            ToolUseID = result.ToolCallId,
                            Content = result.Content,
                        });
                        break;
                    case AgentThinkingPart thinking:
                        blocks.Add(new ThinkingBlockParam
                        {
                            Thinking = thinking.Thinking,
                            Signature = thinking.Signature,
                        });
                        break;
                    case AgentRedactedThinkingPart redacted:
                        blocks.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                        break;
                }
            }

            if (blocks.Count == 0)
            {
                continue;
            }

            messages.Add(new MessageParam
            {
                Role = entry.Role == AgentRole.User ? Role.User : Role.Assistant,
                Content = blocks,
            });
        }

        var parameters = new MessageCreateParams
        {
            Model = options.Model,
            MaxTokens = options.MaxOutputTokensPerTurn,
            Tools = sdkTools,
            Messages = messages,
        };

        var reply = await client.Messages.Create(parameters).WaitAsync(ct);

        var content = new List<AgentPart>();
        foreach (var value in reply.Content.Select(block => (object?)block.Value))
        {
            switch (value)
            {
                case TextBlock text:
                    content.Add(new AgentTextPart(text.Text));
                    break;
                case ToolUseBlock toolUse:
                    content.Add(new AgentToolCallPart(toolUse.ID, toolUse.Name, JsonSerializer.Serialize(toolUse.Input)));
                    break;
                case ThinkingBlock thinking:
                    content.Add(new AgentThinkingPart(thinking.Thinking, thinking.Signature));
                    break;
                case RedactedThinkingBlock redacted:
                    content.Add(new AgentRedactedThinkingPart(redacted.Data));
                    break;
            }
        }

        // A refusal, a cut-off answer or an unknown stop reason is never read as a successful answer.
        var stop = reply.StopReason?.ToString() switch
        {
            "end_turn" => AgentStop.EndTurn,
            "tool_use" => AgentStop.ToolUse,
            "max_tokens" => AgentStop.MaxTokens,
            "refusal" => AgentStop.Refusal,
            _ => AgentStop.Other,
        };

        return new AgentTurn(
            content,
            stop,
            (int)reply.Usage.InputTokens,
            (int)reply.Usage.OutputTokens);
    }

    /// <summary>Tool input as the SDK's input dictionary (the shape the echoed tool_use block must carry).</summary>
    private static Dictionary<string, JsonElement> ToInput(string argumentsJson) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson)
        ?? new Dictionary<string, JsonElement>();
}
