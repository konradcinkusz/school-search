namespace SchoolSearch.Api.Ai;

/// <summary>
/// Search-intent settings, bound from SchoolSearch:Ai:SearchIntent. The model, the kill switch and the daily token
/// budget are shared with the explain feature through <see cref="AiOptions"/> and <see cref="TokenBudget"/>.
/// </summary>
public sealed class SearchIntentOptions
{
    public const string SectionName = "SchoolSearch:Ai:SearchIntent";

    /// <summary>Output cap per call. The answer is a small JSON object, so the cap is low.</summary>
    public int MaxOutputTokens { get; set; } = 300;

    /// <summary>Model calls one client IP may trigger per hour. Cache hits do not count.</summary>
    public int QuestionsPerIpPerHour { get; set; } = 20;
}
