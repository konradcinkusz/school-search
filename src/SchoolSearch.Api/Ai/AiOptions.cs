namespace SchoolSearch.Api.Ai;

/// <summary>
/// AI explain settings, bound from SchoolSearch:Ai. The flat deployment variables AI_ENABLED, AI_MODEL and
/// AI_DAILY_TOKEN_BUDGET override these (see <see cref="AiServiceCollectionExtensions"/>). The API key is deliberately
/// not an option: it never sits in a bindable object, only in <see cref="AiAvailability"/> and the SDK client.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "SchoolSearch:Ai";

    /// <summary>Kill switch (flat env AI_ENABLED). False behaves like a missing key: the endpoint answers 503.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Pinned model id (flat env AI_MODEL). No alias such as "latest" (AI-FEATURES section 2).</summary>
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>Total tokens (input plus output) per UTC day, process-wide (flat env AI_DAILY_TOKEN_BUDGET).</summary>
    public long DailyTokenBudget { get; set; } = 200_000;

    /// <summary>Output cap per call. Adaptive thinking counts against it.</summary>
    public int MaxOutputTokens { get; set; } = 1500;

    /// <summary>How long a grounded explanation is served from cache.</summary>
    public int CacheHours { get; set; } = 24;

    /// <summary>Model calls one client IP may trigger per hour. Cache hits do not count.</summary>
    public int ExplainsPerIpPerHour { get; set; } = 10;
}

/// <summary>
/// Whether the explain feature can run at all: the kill switch is on and a key is configured. Computed once at
/// startup. <see cref="Detail"/> is shown on /health; it never contains the key.
/// </summary>
public sealed record AiAvailability(bool IsAvailable, string Detail);
