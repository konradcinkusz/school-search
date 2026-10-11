using SchoolSearch.Api.Scoring;
using SchoolSearch.Api.Services;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// The structured values an explanation may talk about. Built only from the SchoolProfile the Api computed for the
/// school; it carries no school name, operator, address or user data. Suppressed or missing scores carry no number.
/// </summary>
public sealed record ExplainFacts(
    ScoreBlock? Score,
    string ScoreStatus,
    double? CoveragePercent,
    int? CohortSize,
    int PublicationThreshold,
    double CoverageFloorPercent)
{
    /// <summary>
    /// Maps a profile to facts. For any status other than published, the score, interval bounds are removed from the
    /// block so that no score number can reach the prompt or the response, whatever the engine returned.
    /// </summary>
    public static ExplainFacts From(SchoolProfile profile, int? examYear)
    {
        var block = profile.Score;
        if (block is not null && profile.ScoreStatus != ScoreStatuses.Published)
        {
            block = block with { Score = null, IntervalLow = null, IntervalHigh = null };
        }

        double? coverage = examYear is int year
            ? profile.Trend.FirstOrDefault(point => point.ExamYear == year)?.CoveragePercent
            : null;

        return new ExplainFacts(
            block,
            profile.ScoreStatus,
            coverage,
            block?.CohortSize,
            ScoringPolicy.PublicationThreshold,
            Math.Round(ScoringPolicy.CoverageFloor * 100d, 0));
    }
}

/// <summary>The response of <c>POST /api/v1/schools/{slug}/explain</c>.</summary>
public sealed record ExplainResponse(
    string Slug,
    string Text,
    string Label,
    string Model,
    string MethodologyVersion,
    int? ExamYear,
    ExplainFacts Facts,
    DateTimeOffset GeneratedAt,
    bool Cached);

/// <summary>Body of the explain request. Optional; an empty body means the defaults.</summary>
public sealed record ExplainRequest(string? Lang);

public enum ExplainStatus
{
    Ok,
    NotFound,
    Unavailable,
    BudgetExhausted,
    RateLimited,
    OutputRejected,
}

/// <summary>Result of one explain call. <see cref="Response"/> is set only for <see cref="ExplainStatus.Ok"/>.</summary>
public sealed record ExplainOutcome(ExplainStatus Status, ExplainResponse? Response = null);
