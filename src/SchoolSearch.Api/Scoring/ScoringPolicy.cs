namespace SchoolSearch.Api.Scoring;

/// <summary>
/// The single home of every scoring constant. Changing any value here is a methodology change: bump
/// <see cref="MethodologyVersion"/> and record an ADR (ADR-0009 for the threshold).
/// </summary>
public static class ScoringPolicy
{
    /// <summary>Version stamped on every score produced by this engine.</summary>
    public const string MethodologyVersion = "1.0.0";

    /// <summary>
    /// Minimum number of pupils who sat the exam before a score is published (ADR-0009). Counts pupils
    /// who SAT, not class headcount. THE only place the threshold lives. Below it the engine suppresses
    /// the score with <c>insufficient_cohort</c> (METRIC-ETHICS §3: no number without its cohort).
    /// </summary>
    public const int PublicationThreshold = 9;

    /// <summary>
    /// Coverage (sat / headcount) at or above which no discount applies. Below it the score is
    /// discounted linearly (METRIC-ETHICS §2: the counter-metric is blended into the exam component).
    /// </summary>
    public const double CoverageFloor = 0.8;

    /// <summary>Two-sided 95% normal quantile used for the reported interval (METRIC-ETHICS §3).</summary>
    public const double IntervalZ = 1.96;
}
