namespace SchoolSearch.Contracts.Public;

/// <summary>
/// A score as served to the public. Always complete: cohort size, methodology version, year and provenance
/// are present whenever a block exists (METRIC-ETHICS §3). Suppressed blocks carry a null score and a reason.
/// </summary>
/// <param name="Score">Percentage points versus the national mean (0 = national average), or null when suppressed.</param>
/// <param name="Reason">"limited" for 5 to 8 pupils (no score, no rank); "insufficient_cohort" below 5; null when published.</param>
/// <param name="CohortSize">Pupils who sat the exam (the cohort behind the score).</param>
/// <param name="IntervalLow">Lower bound of the 95% interval, or null when suppressed.</param>
/// <param name="IntervalHigh">Upper bound of the 95% interval, or null when suppressed.</param>
/// <param name="MethodologyVersion">Methodology version that produced the value.</param>
/// <param name="ExamYear">Exam year the score describes.</param>
/// <param name="AsOf">ISO date (yyyy-MM-dd) the underlying exam result describes.</param>
/// <param name="Source">Name of the source of the underlying exam result.</param>
public sealed record ScoreBlock(
    double? Score,
    string? Reason,
    int CohortSize,
    double? IntervalLow,
    double? IntervalHigh,
    string MethodologyVersion,
    int ExamYear,
    string AsOf,
    string Source);
