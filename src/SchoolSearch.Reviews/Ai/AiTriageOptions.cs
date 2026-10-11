namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// AI moderation triage settings, bound from Reviews:Ai. The flat deployment variables AI_ENABLED, AI_MODEL and
/// AI_DAILY_TOKEN_BUDGET override these (see <see cref="AiTriageServiceCollectionExtensions"/>). The API key is
/// deliberately not an option: it lives only in <see cref="AiTriageAvailability"/> and the SDK client.
/// </summary>
public sealed class AiTriageOptions
{
    public const string SectionName = "Reviews:Ai";

    /// <summary>Kill switch (flat env AI_ENABLED). False leaves the worker idle and the queue without labels (P8).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Pinned model id (flat env AI_MODEL). No alias such as "latest".</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Total tokens (input plus output) per UTC day, process-wide (flat env AI_DAILY_TOKEN_BUDGET).</summary>
    public long DailyTokenBudget { get; set; } = 100_000;

    /// <summary>Output cap per call. Adaptive thinking counts against it, as in the Api explain feature.</summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>Largest title plus body sent to the model. Longer reviews get label unclear without a call (no silent truncation).</summary>
    public int MaxInputChars { get; set; } = 4000;

    /// <summary>Model calls per minute for this process, on demand and from the worker together.</summary>
    public int TriagesPerMinute { get; set; } = 10;

    /// <summary>Pending reviews fetched per worker pass.</summary>
    public int BatchSize { get; set; } = 5;

    /// <summary>Seconds between worker passes when there is nothing to do.</summary>
    public int IdlePollSeconds { get; set; } = 30;
}

/// <summary>
/// Whether AI triage can run: the kill switch is on and a key is configured. Computed once at startup.
/// <see cref="Detail"/> is shown on /health and never contains the key.
/// </summary>
public sealed record AiTriageAvailability(bool IsAvailable, string Detail);
