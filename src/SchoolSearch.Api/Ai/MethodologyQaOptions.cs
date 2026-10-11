namespace SchoolSearch.Api.Ai;

/// <summary>
/// Methodology Q&amp;A settings, bound from SchoolSearch:Ai:MethodologyQa. The model, the daily token budget and the
/// kill switch are shared with the explain feature (<see cref="AiOptions"/>, <see cref="AiAvailability"/>, <see cref="TokenBudget"/>).
/// </summary>
public sealed class MethodologyQaOptions
{
    public const string SectionName = "SchoolSearch:Ai:MethodologyQa";

    /// <summary>Model calls one client IP may trigger per hour. Cache hits do not count.</summary>
    public int AnswersPerIpPerHour { get; set; } = 20;

    /// <summary>How long a grounded answer is served from cache, per normalised question and methodology version.</summary>
    public int CacheHours { get; set; } = 24;
}
