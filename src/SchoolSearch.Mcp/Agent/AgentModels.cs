using System.Text.Json;

namespace SchoolSearch.Mcp.Agent;

/// <summary>Who wrote a conversation message.</summary>
public enum AgentRole
{
    User,
    Assistant,
}

/// <summary>
/// One part of a conversation message. SDK-agnostic: <see cref="AnthropicAgentLlm"/> is the only code that maps these
/// to and from SDK blocks, so an SDK change is a one-file fix.
/// </summary>
public abstract record AgentPart;

/// <summary>Plain text, from the user or from the model.</summary>
public sealed record AgentTextPart(string Text) : AgentPart;

/// <summary>A tool the model asked to run. <paramref name="ArgumentsJson"/> is the JSON object the model sent.</summary>
public sealed record AgentToolCallPart(string Id, string Name, string ArgumentsJson) : AgentPart;

/// <summary>The answer to one tool call. <paramref name="Content"/> is already wrapped as untrusted data.</summary>
public sealed record AgentToolResultPart(string ToolCallId, string Content) : AgentPart;

/// <summary>Extended-thinking block, echoed back unchanged (the API rejects an altered signature).</summary>
public sealed record AgentThinkingPart(string Thinking, string Signature) : AgentPart;

/// <summary>Redacted extended-thinking block, echoed back unchanged.</summary>
public sealed record AgentRedactedThinkingPart(string Data) : AgentPart;

/// <summary>One conversation message: a role and its parts.</summary>
public sealed record AgentMessage(AgentRole Role, IReadOnlyList<AgentPart> Parts);

/// <summary>A tool offered to the model. <paramref name="Properties"/> are JSON Schema property definitions by name.</summary>
public sealed record AgentToolDef(
    string Name,
    string Description,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyList<string> Required);

/// <summary>Why the model stopped one turn, reduced to the cases the loop distinguishes.</summary>
public enum AgentStop
{
    EndTurn,
    ToolUse,
    MaxTokens,
    Refusal,
    Other,
}

/// <summary>One model turn: its content parts, its stop reason and its token usage.</summary>
public sealed record AgentTurn(IReadOnlyList<AgentPart> Content, AgentStop Stop, int InputTokens, int OutputTokens);

/// <summary>
/// The one seam between the agent loop and the model provider. Tests use a scripted fake; the only implementation
/// that talks to Anthropic is <see cref="AnthropicAgentLlm"/>.
/// </summary>
public interface IAgentLlm
{
    /// <summary>Sends the conversation and the offered tools, and returns the next model turn.</summary>
    Task<AgentTurn> NextAsync(
        IReadOnlyList<AgentMessage> history,
        IReadOnlyList<AgentToolDef> tools,
        string system,
        CancellationToken ct);
}
