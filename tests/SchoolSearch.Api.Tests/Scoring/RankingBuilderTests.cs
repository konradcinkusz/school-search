using System.Reflection;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Contracts;

namespace SchoolSearch.Api.Tests.Scoring;

public sealed class RankingBuilderTests
{
    private static ScoreDto Published(double score) =>
        new(score, null, 50, null, null, ScoringPolicy.MethodologyVersion);

    private static ScoreDto Suppressed(int cohort) =>
        new(null, ScoreDto.InsufficientCohortReason, cohort, null, null, ScoringPolicy.MethodologyVersion);

    private static ScoreDto Limited(int cohort) =>
        new(null, ScoreDto.LimitedReason, cohort, null, null, ScoringPolicy.MethodologyVersion);

    [Fact]
    public void Limited_schools_are_never_ranked_even_beside_higher_published_scores()
    {
        // Cohorts 5 to 8 (ADR-0009) carry no score and no rank: they are counted as unranked, never placed.
        var schools = new (string Slug, ScoreDto Score)[]
        {
            ("school-l1", Limited(5)),
            ("school-p1", Published(-3.0)),
            ("school-l2", Limited(8)),
            ("school-p2", Published(20.0)),
        };

        var ranked = RankingBuilder.RankDescending(schools, out var unranked);

        Assert.Equal(new[] { "school-p2", "school-p1" }, ranked.Select(r => r.Slug).ToArray());
        Assert.Equal(new[] { 1, 2 }, ranked.Select(r => r.Rank).ToArray());
        Assert.Equal(new[] { "school-l1", "school-l2" }, unranked.Select(u => u.Slug).ToArray());
        Assert.All(unranked, u => Assert.Equal(ScoreDto.LimitedReason, u.Score.Reason));
        Assert.All(unranked, u => Assert.Null(u.Score.Score));
    }

    [Fact]
    public void Ranks_descending_and_tied_schools_share_a_rank_with_stable_slug_order()
    {
        var schools = new (string Slug, ScoreDto Score)[]
        {
            ("school-c", Published(5.0)),
            ("school-b", Published(12.0)),
            ("school-d", Suppressed(3)),
            ("school-a", Published(12.0)),
        };

        var ranked = RankingBuilder.RankDescending(schools, out _);

        Assert.Equal(3, ranked.Count);
        Assert.Equal(new[] { "school-a", "school-b", "school-c" }, ranked.Select(r => r.Slug).ToArray());
        Assert.Equal(new[] { 1, 1, 3 }, ranked.Select(r => r.Rank).ToArray());
        Assert.Equal(new[] { 12.0, 12.0, 5.0 }, ranked.Select(r => r.Score.Score!.Value).ToArray());
    }

    [Fact]
    public void Ties_below_the_top_share_the_position_of_the_first_tied_school()
    {
        var schools = new (string Slug, ScoreDto Score)[]
        {
            ("school-x", Published(4.0)),
            ("school-y", Published(9.0)),
            ("school-z", Published(4.0)),
        };

        var ranked = RankingBuilder.RankDescending(schools, out _);

        Assert.Equal(new[] { "school-y", "school-x", "school-z" }, ranked.Select(r => r.Slug).ToArray());
        Assert.Equal(new[] { 1, 2, 2 }, ranked.Select(r => r.Rank).ToArray());
    }

    [Fact]
    public void Suppressed_schools_go_to_unranked_and_are_not_ranked()
    {
        var schools = new (string Slug, ScoreDto Score)[]
        {
            ("school-p", Published(7.0)),
            ("school-q", Suppressed(8)),
            ("school-r", Suppressed(0)),
        };

        var ranked = RankingBuilder.RankDescending(schools, out var unranked);

        Assert.Single(ranked);
        Assert.Equal("school-p", ranked[0].Slug);
        Assert.Equal(1, ranked[0].Rank);

        Assert.Equal(2, unranked.Count);
        Assert.Equal(new[] { "school-q", "school-r" }, unranked.Select(u => u.Slug).ToArray());
        Assert.All(unranked, u => Assert.Equal(ScoreDto.InsufficientCohortReason, u.Score.Reason));
        Assert.Equal(new[] { 8, 0 }, unranked.Select(u => u.Score.CohortSize).ToArray());
    }

    [Fact]
    public void All_suppressed_input_yields_no_ranks_and_every_school_unranked()
    {
        var schools = new (string Slug, ScoreDto Score)[]
        {
            ("school-m", Suppressed(2)),
            ("school-n", Suppressed(4)),
        };

        var ranked = RankingBuilder.RankDescending(schools, out var unranked);

        Assert.Empty(ranked);
        Assert.Equal(2, unranked.Count);
    }

    [Fact]
    public void No_member_of_the_scoring_namespace_exposes_an_ascending_bottom_worst_or_reverse_api()
    {
        string[] forbidden = { "Ascending", "Bottom", "Worst", "Reverse" };
        var scoringTypes = typeof(RankingBuilder).Assembly.GetTypes()
            .Where(t => t.Namespace == "SchoolSearch.Api.Scoring")
            .ToArray();

        Assert.Contains(typeof(RankingBuilder), scoringTypes);
        Assert.Contains(typeof(ScoringEngine), scoringTypes);

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var offenders = scoringTypes
            .SelectMany(t => t.GetMembers(all).Select(m => $"{t.Name}.{m.Name}"))
            .Where(name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Empty(offenders);
    }
}
