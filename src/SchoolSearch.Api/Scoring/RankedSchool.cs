using SchoolSearch.Contracts;

namespace SchoolSearch.Api.Scoring;

/// <summary>
/// A school placed in a ranking. Only published scores (non-null) are ever ranked.
/// </summary>
/// <param name="Rank">Competition rank: ties share a rank and the next rank is skipped (1, 1, 3).</param>
/// <param name="Slug">The school's public slug.</param>
/// <param name="Score">The published score, carried whole with its cohort size, interval and methodology version.</param>
public sealed record RankedSchool(int Rank, string Slug, ScoreDto Score);
