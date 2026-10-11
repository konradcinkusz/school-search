namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// Settings of the tool-use agent. Filled by <see cref="AgentServiceCollectionExtensions.AddAgent"/> from the flat
/// deployment variables (AI_ENABLED, AI_MODEL, AI_DAILY_TOKEN_BUDGET, AGENT_REQUESTS_PER_IP_PER_MINUTE). The API key is
/// deliberately not an option: it is passed only to the SDK client.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>True when the agent may run: the kill switch is not off and a key is set. Computed at startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>Pinned model id. No alias such as "latest" (AI-FEATURES section 2).</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Total tokens (input plus output) per UTC day, process-wide.</summary>
    public long DailyTokenBudget { get; set; } = 200_000;

    /// <summary>Hard cap on input plus output tokens of one request, summed over its model turns.</summary>
    public long MaxTokensPerRequest { get; set; } = 60_000;

    /// <summary>Model turns allowed per request.</summary>
    public int MaxModelTurns { get; set; } = 6;

    /// <summary>Tool calls executed per request, over all turns.</summary>
    public int MaxToolCalls { get; set; } = 8;

    /// <summary>Overall deadline of one request, including every model turn and tool call.</summary>
    public TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Output cap per model turn. Adaptive thinking counts against it.</summary>
    public int MaxOutputTokensPerTurn { get; set; } = 2000;

    /// <summary>Agent requests one client IP may send per minute. Rejections are answered before any model call.</summary>
    public int RequestsPerIpPerMinute { get; set; } = 6;
}
