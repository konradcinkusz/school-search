using SchoolSearch.Contracts;

namespace SchoolSearch.Api.Scoring;

/// <summary>
/// Pure scoring function: exam results of one school against the national baseline of the same year.
/// No I/O, no clock. The unit of evaluation is the school (the artifact), never a person (METRIC-ETHICS §5).
/// </summary>
public static class ScoringEngine
{
    /// <summary>
    /// Scores one school. Returns a <see cref="ScoreDto"/> that always carries cohort size and
    /// methodology version; a suppressed result has a null score and reason <c>insufficient_cohort</c>,
    /// never 0 and never a guess (METRIC-ETHICS §3).
    /// </summary>
    /// <exception cref="ArgumentException">Year mismatch, duplicate subjects, or invalid values.</exception>
    /// <exception cref="InvalidOperationException">
    /// No subject is present in both input and baseline. This is a data bug and is not hidden as suppression.
    /// </exception>
    public static ScoreDto Score(SchoolExamInput input, NationalBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(input.Subjects);
        ArgumentNullException.ThrowIfNull(baseline.Subjects);

        if (input.Year != baseline.Year)
        {
            throw new ArgumentException(
                $"Input year {input.Year} does not match baseline year {baseline.Year}.",
                nameof(baseline));
        }

        // Index both sides by subject. A duplicate is a data bug, so it fails loudly.
        var baselineBySubject = new Dictionary<Subject, SubjectBaseline>();
        foreach (var subjectBaseline in baseline.Subjects)
        {
            ArgumentNullException.ThrowIfNull(subjectBaseline);
            if (!baselineBySubject.TryAdd(subjectBaseline.Subject, subjectBaseline))
            {
                throw new ArgumentException(
                    $"Baseline lists subject {subjectBaseline.Subject} more than once.",
                    nameof(baseline));
            }
        }

        var seenSubjects = new HashSet<Subject>();
        var paired = new List<(SubjectResult Result, SubjectBaseline Reference)>();
        foreach (var result in input.Subjects)
        {
            ArgumentNullException.ThrowIfNull(result);

            if (!seenSubjects.Add(result.Subject))
            {
                throw new ArgumentException(
                    $"Input lists subject {result.Subject} more than once.",
                    nameof(input));
            }

            if (!double.IsFinite(result.MeanPercent) || result.MeanPercent < 0d || result.MeanPercent > 100d)
            {
                throw new ArgumentException(
                    $"Mean for {result.Subject} must be a finite percentage between 0 and 100.",
                    nameof(input));
            }

            if (result.PupilsSat < 0)
            {
                throw new ArgumentException(
                    $"Pupils who sat {result.Subject} cannot be negative.",
                    nameof(input));
            }

            if (baselineBySubject.TryGetValue(result.Subject, out var reference))
            {
                paired.Add((result, reference));
            }
        }

        // Only subjects present in BOTH input and baseline are scored. If none overlap, the input or the
        // baseline is wrong; suppressing would hide that, so this throws.
        if (paired.Count == 0)
        {
            throw new InvalidOperationException(
                "No subject is present in both the school input and the national baseline.");
        }

        // A subject with no pupils has no measurement. It would divide by zero in the interval, so it is a
        // data bug and is rejected rather than silently dropped.
        foreach (var (result, _) in paired)
        {
            if (result.PupilsSat == 0)
            {
                throw new ArgumentException(
                    $"Subject {result.Subject} has zero pupils who sat it; omit the subject instead.",
                    nameof(input));
            }
        }

        // Cohort = the largest number of pupils who sat any subject. Suppression is decided on this
        // number only (ADR-0009, METRIC-ETHICS §3). It is always written into the result.
        int cohort = 0;
        foreach (var result in input.Subjects)
        {
            cohort = Math.Max(cohort, result.PupilsSat);
        }

        if (input.ClassVIIIHeadcount <= 0 || cohort < ScoringPolicy.PublicationThreshold)
        {
            return new ScoreDto(
                null,
                ScoreDto.InsufficientCohortReason,
                cohort,
                null,
                null,
                ScoringPolicy.MethodologyVersion);
        }

        // Counter-metric (METRIC-ETHICS §2): coverage = sat / headcount, clamped to [0, 1]. It is blended
        // into the exam component as a multiplier on every subject mean, not shown as a side badge. Because
        // the factor scales the mean, the score cannot rise by lowering coverage: d(score)/d(factor) equals
        // the mean, which is never negative.
        double coverage = Math.Clamp((double)cohort / input.ClassVIIIHeadcount, 0d, 1d);
        double factor = coverage >= ScoringPolicy.CoverageFloor
            ? 1d
            : coverage / ScoringPolicy.CoverageFloor;

        // Equal weights across the k subjects present in both sides, in percentage points versus the
        // national mean (0 = national average). METRIC-ETHICS §2: equal weights, modest pressurable component.
        double sumDeviation = 0d;
        double sumSquaredStandardError = 0d;
        foreach (var (result, reference) in paired)
        {
            double effective = result.MeanPercent * factor;
            sumDeviation += effective - reference.NationalMeanPercent;

            // Per-subject standard error: pupil SD / sqrt(n) scaled by the same coverage factor, so a
            // low-coverage school also carries a wider interval (METRIC-ETHICS §3).
            double standardError = reference.PupilStdDevPoints / Math.Sqrt((double)result.PupilsSat) * factor;
            sumSquaredStandardError += standardError * standardError;
        }

        int k = paired.Count;
        double score = sumDeviation / k;
        double se = Math.Sqrt(sumSquaredStandardError) / k;
        double margin = ScoringPolicy.IntervalZ * se;

        return new ScoreDto(
            score,
            null,
            cohort,
            score - margin,
            score + margin,
            ScoringPolicy.MethodologyVersion);
    }
}
