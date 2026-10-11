namespace SchoolSearch.Billing.Ai;

/// <summary>
/// AI profile-copy assistant settings, bound from Billing:Ai. The flat deployment variables AI_ENABLED, AI_MODEL and
/// AI_DAILY_TOKEN_BUDGET override these (see <see cref="AiDraftServiceCollectionExtensions"/>). The API key is
/// deliberately not an option: it lives only in <see cref="DraftAvailability"/> and the SDK client.
/// </summary>
public sealed class AiDraftOptions
{
    public const string SectionName = "Billing:Ai";

    /// <summary>Kill switch (flat env AI_ENABLED). False behaves like a missing key: the endpoint answers 503 (P8).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Pinned model id (flat env AI_MODEL). No alias such as "latest".</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Total tokens (input plus output) per UTC day, process-wide (flat env AI_DAILY_TOKEN_BUDGET).</summary>
    public long DailyTokenBudget { get; set; } = 100_000;

    /// <summary>Output cap per call. Adaptive thinking counts against it, as in the Api explain feature.</summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>Draft requests one owner (subject hash) may make per hour. A request that reaches the model takes one.</summary>
    public int DraftsPerSubjectPerHour { get; set; } = 10;
}

/// <summary>
/// Whether the draft assistant can run at all: the kill switch is on and a key is configured. Computed once at startup.
/// <see cref="Detail"/> is shown on /health and never contains the key.
/// </summary>
public sealed record DraftAvailability(bool IsAvailable, string Detail);
