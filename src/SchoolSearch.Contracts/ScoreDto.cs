namespace SchoolSearch.Contracts;

/// <summary>
/// A published score, always carried with its cohort size, interval and methodology version (METRIC-ETHICS gate).
/// The type contract: a ScoreDto cannot be constructed without a cohort size and a methodology version.
/// Below the cohort threshold, <see cref="Score"/> is null and <see cref="Reason"/> is
/// <see cref="InsufficientCohortReason"/>. It is never 0 or a guess.
/// </summary>
public sealed class ScoreDto
{
    /// <summary>The only reason value a null score may carry in v1.</summary>
    public const string InsufficientCohortReason = "insufficient_cohort";

    public ScoreDto(
        double? score,
        string? reason,
        int cohortSize,
        double? intervalLow,
        double? intervalHigh,
        string methodologyVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cohortSize);
        ArgumentException.ThrowIfNullOrWhiteSpace(methodologyVersion);

        if ((intervalLow is null) != (intervalHigh is null))
        {
            throw new ArgumentException("The interval bounds must both be set or both be null.");
        }

        if (intervalLow is double low && intervalHigh is double high && low > high)
        {
            throw new ArgumentException("The interval low bound cannot exceed the high bound.");
        }

        if (score is null)
        {
            if (reason != InsufficientCohortReason)
            {
                throw new ArgumentException($"A null score must carry the reason '{InsufficientCohortReason}'.");
            }

            if (intervalLow is not null)
            {
                throw new ArgumentException("A suppressed score cannot carry an interval.");
            }
        }
        else if (reason is not null)
        {
            throw new ArgumentException("A published score cannot carry a reason.");
        }

        Score = score;
        Reason = reason;
        CohortSize = cohortSize;
        IntervalLow = intervalLow;
        IntervalHigh = intervalHigh;
        MethodologyVersion = methodologyVersion;
    }

    /// <summary>The score, or null when suppressed.</summary>
    public double? Score { get; }

    /// <summary>Null when a score is published; otherwise <see cref="InsufficientCohortReason"/>.</summary>
    public string? Reason { get; }

    /// <summary>The number of pupils behind the score. Always present.</summary>
    public int CohortSize { get; }

    public double? IntervalLow { get; }

    public double? IntervalHigh { get; }

    /// <summary>The methodology version that produced this value. Always present.</summary>
    public string MethodologyVersion { get; }
}
