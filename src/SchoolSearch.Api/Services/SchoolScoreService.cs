using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Services;

/// <summary>Values of <c>scoreStatus</c> in the public contract.</summary>
public static class ScoreStatuses
{
    public const string Published = "published";
    public const string InsufficientCohort = "insufficient_cohort";
    public const string NoExamData = "no_exam_data";
    public const string NoBaseline = "no_baseline";
}

/// <summary>
/// The outcome of scoring one school in one exam year. <see cref="Dto"/> is set only when the engine ran
/// (published or insufficient_cohort); it is null for no_exam_data and no_baseline, where no number exists.
/// </summary>
public sealed record SchoolScore(
    string ScoreStatus,
    int? ExamYear,
    ScoreDto? Dto,
    double? CoveragePercent,
    DateOnly? AsOf,
    string? Source)
{
    public static SchoolScore Missing(string status, int? examYear) =>
        new(status, examYear, null, null, null, null);
}

/// <summary>
/// Glue between the stored exam data and the pure <see cref="ScoringEngine"/>. Loads are batched per exam year
/// (one query for results, one for cohorts, one for baselines), never one query per school.
/// Year convention: ExamResult.ExamYear = Y is measured against the class VIII cohort of SchoolYear = Y - 1.
/// </summary>
public sealed class SchoolScoreService(CatalogDbContext db)
{
    public const string HeadcountClassLevel = "VIII";

    private static readonly IReadOnlyDictionary<string, NationalBaselineRow> NoBaselines =
        new Dictionary<string, NationalBaselineRow>(StringComparer.Ordinal);

    /// <summary>The default exam year: the newest year with a national baseline. Null when no baseline exists.</summary>
    public async Task<int?> ResolveCurrentYearAsync(CancellationToken ct) =>
        await db.NationalBaselines.AsNoTracking().MaxAsync(b => (int?)b.ExamYear, ct);

    /// <summary>
    /// Scores the schools that have exam results in <paramref name="examYear"/>. Pass null to score every school
    /// with results. Schools absent from the returned dictionary have no exam data for that year.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, SchoolScore>> ScoreAsync(
        int examYear,
        IReadOnlyCollection<long>? schoolIds,
        CancellationToken ct)
    {
        IQueryable<ExamResult> resultQuery = db.ExamResults.AsNoTracking().Where(e => e.ExamYear == examYear);
        IQueryable<ClassCohort> cohortQuery = db.ClassCohorts.AsNoTracking()
            .Where(c => c.SchoolYear == examYear - 1 && c.ClassLevel == HeadcountClassLevel);

        if (schoolIds is not null)
        {
            var ids = schoolIds.ToArray();
            if (ids.Length == 0)
            {
                return new Dictionary<long, SchoolScore>();
            }

            resultQuery = resultQuery.Where(e => ids.Contains(e.SchoolId));
            cohortQuery = cohortQuery.Where(c => ids.Contains(c.SchoolId));
        }

        var results = await resultQuery.ToListAsync(ct);
        var headcounts = (await cohortQuery.Select(c => new { c.SchoolId, c.Pupils }).ToListAsync(ct))
            .ToDictionary(c => c.SchoolId, c => c.Pupils);
        var baselines = await BaselinesForYearAsync(examYear, ct);

        var output = new Dictionary<long, SchoolScore>();
        foreach (var group in results.GroupBy(r => r.SchoolId))
        {
            headcounts.TryGetValue(group.Key, out var headcount);
            output[group.Key] = Evaluate(examYear, group.ToList(), headcount, baselines);
        }

        return output;
    }

    /// <summary>One school's history: a point per exam year it has results for, oldest first.</summary>
    public async Task<IReadOnlyList<TrendPoint>> TrendAsync(long schoolId, CancellationToken ct)
    {
        var rows = await db.ExamResults.AsNoTracking()
            .Where(e => e.SchoolId == schoolId)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return Array.Empty<TrendPoint>();
        }

        var cohorts = await db.ClassCohorts.AsNoTracking()
            .Where(c => c.SchoolId == schoolId && c.ClassLevel == HeadcountClassLevel)
            .ToListAsync(ct);
        // Exam year = school year + 1 (see the year convention above). The key is unique per school and level.
        var headcountByExamYear = cohorts.ToDictionary(c => c.SchoolYear + 1, c => c.Pupils);

        var years = rows.Select(r => r.ExamYear).Distinct().OrderBy(y => y).ToArray();
        var baselineRows = await db.NationalBaselines.AsNoTracking()
            .Where(b => years.Contains(b.ExamYear))
            .ToListAsync(ct);
        var baselinesByYear = baselineRows
            .GroupBy(b => b.ExamYear)
            .ToDictionary(g => g.Key, g => g.ToDictionary(b => b.Subject, StringComparer.Ordinal));

        var points = new List<TrendPoint>(years.Length);
        foreach (var year in years)
        {
            var yearRows = rows
                .Where(r => r.ExamYear == year)
                .OrderBy(r => r.Subject, StringComparer.Ordinal)
                .ToList();

            headcountByExamYear.TryGetValue(year, out var headcount);
            IReadOnlyDictionary<string, NationalBaselineRow> yearBaselines =
                baselinesByYear.TryGetValue(year, out var found) ? found : NoBaselines;

            var score = Evaluate(year, yearRows, headcount, yearBaselines);
            var subjects = yearRows
                .Select(r => new SubjectRow(r.Subject, r.MeanPercent, r.PupilsSat))
                .ToList();

            points.Add(new TrendPoint(year, score.ScoreStatus, ToBlock(score), score.CoveragePercent, subjects));
        }

        return points;
    }

    /// <summary>
    /// Maps a score to its public block. Null when the engine did not run (no_exam_data, no_baseline): the contract
    /// never carries a block without cohort, methodology and provenance, so none is invented for those.
    /// </summary>
    public static ScoreBlock? ToBlock(SchoolScore? score)
    {
        var dto = score?.Dto;
        if (score is null
            || dto is null
            || score.ExamYear is not int examYear
            || score.AsOf is not DateOnly asOf
            || score.Source is not string source)
        {
            return null;
        }

        return new ScoreBlock(
            dto.Score,
            dto.Reason,
            dto.CohortSize,
            dto.IntervalLow,
            dto.IntervalHigh,
            dto.MethodologyVersion,
            examYear,
            asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            source);
    }

    /// <summary>
    /// Scores one school-year from in-memory rows. Shared by the batch path and the trend path so both apply the
    /// same rules: non-measured subjects are ignored, a missing or unusable baseline gives no_baseline, and the
    /// engine decides publication (suppression is never decided here).
    /// </summary>
    internal static SchoolScore Evaluate(
        int examYear,
        IReadOnlyList<ExamResult> rows,
        int headcount,
        IReadOnlyDictionary<string, NationalBaselineRow> baselines)
    {
        // A subject with no pupils who sat it is not a measurement; the engine rejects it, so it is left out here.
        var measured = new List<(ExamResult Row, Subject Subject)>();
        foreach (var row in rows)
        {
            if (row.PupilsSat <= 0 || !TryMapSubject(row.Subject, out var subject))
            {
                continue;
            }

            measured.Add((row, subject));
        }

        if (measured.Count == 0)
        {
            return SchoolScore.Missing(ScoreStatuses.NoExamData, examYear);
        }

        // Reference subjects for this school. A null standard deviation is never guessed: the school gets no_baseline.
        var referenceSubjects = new List<SubjectBaseline>();
        foreach (var (row, subject) in measured)
        {
            if (!baselines.TryGetValue(row.Subject, out var reference))
            {
                continue;
            }

            double? standardDeviation = reference.PupilStdDevPoints;
            if (standardDeviation is null
                || !double.IsFinite(standardDeviation.Value)
                || standardDeviation.Value <= 0d)
            {
                return SchoolScore.Missing(ScoreStatuses.NoBaseline, examYear);
            }

            referenceSubjects.Add(new SubjectBaseline(subject, reference.NationalMeanPercent, standardDeviation.Value));
        }

        if (referenceSubjects.Count == 0)
        {
            return SchoolScore.Missing(ScoreStatuses.NoBaseline, examYear);
        }

        var subjects = measured
            .Select(m => new SubjectResult(m.Subject, m.Row.MeanPercent, m.Row.PupilsSat))
            .ToList();

        var input = new SchoolExamInput(examYear, subjects, headcount);
        var baseline = new NationalBaseline(examYear, referenceSubjects);
        var dto = ScoringEngine.Score(input, baseline);

        // Source and date come from the newest stored exam result of this school-year.
        var newest = measured.OrderByDescending(m => m.Row.Provenance.AsOf).First().Row.Provenance;
        double? coverage = headcount > 0
            ? Math.Round(Math.Min(100d, dto.CohortSize * 100d / headcount), 1)
            : (double?)null;

        return new SchoolScore(
            dto.Score is null ? ScoreStatuses.InsufficientCohort : ScoreStatuses.Published,
            examYear,
            dto,
            coverage,
            newest.AsOf,
            newest.Source);
    }

    private async Task<IReadOnlyDictionary<string, NationalBaselineRow>> BaselinesForYearAsync(int examYear, CancellationToken ct)
    {
        var rows = await db.NationalBaselines.AsNoTracking()
            .Where(b => b.ExamYear == examYear)
            .ToListAsync(ct);

        return rows.ToDictionary(b => b.Subject, StringComparer.Ordinal);
    }

    internal static bool TryMapSubject(string code, out Subject subject)
    {
        switch (code)
        {
            case "polish":
                subject = Subject.Polish;
                return true;
            case "mathematics":
                subject = Subject.Mathematics;
                return true;
            case "foreign_language":
                subject = Subject.ForeignLanguage;
                return true;
            default:
                subject = default;
                return false;
        }
    }
}
