using SchoolSearch.Contracts;

namespace SchoolSearch.Api.Scoring;

/// <summary>
/// Builds the descending ranking of published scores.
/// Anti-goal (METRIC-ETHICS §1, enforced by absence): there is no ascending or bottom ranking API, and
/// suppressed schools are never ranked, so no school is placed at the bottom by a missing value.
/// </summary>
public static class RankingBuilder
{
    /// <summary>
    /// Ranks schools with a published score, highest first. Ties share a competition rank (1, 1, 3) and are
    /// ordered by slug (ordinal) so the output is deterministic. Suppressed schools (null score) are returned
    /// in <paramref name="unranked"/>, ordered by slug, and are not ranked.
    /// </summary>
    /// <exception cref="ArgumentException">Blank or duplicate slug, or a non-finite published score.</exception>
    public static IReadOnlyList<RankedSchool> RankDescending(
        IEnumerable<(string Slug, ScoreDto Score)> schools,
        out IReadOnlyList<(string Slug, ScoreDto Score)> unranked)
    {
        ArgumentNullException.ThrowIfNull(schools);

        var seenSlugs = new HashSet<string>(StringComparer.Ordinal);
        var published = new List<(string Slug, ScoreDto Score, double Value)>();
        var suppressed = new List<(string Slug, ScoreDto Score)>();

        foreach (var (slug, score) in schools)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(slug);
            ArgumentNullException.ThrowIfNull(score);

            if (!seenSlugs.Add(slug))
            {
                throw new ArgumentException($"School slug '{slug}' appears more than once.", nameof(schools));
            }

            if (score.Score is double value)
            {
                if (!double.IsFinite(value))
                {
                    throw new ArgumentException($"Score for '{slug}' is not a finite number.", nameof(schools));
                }

                published.Add((slug, score, value));
            }
            else
            {
                // METRIC-ETHICS §3: a suppressed score is never ranked and never treated as 0.
                suppressed.Add((slug, score));
            }
        }

        // Descending by score, then ascending by slug for a stable, deterministic order.
        published.Sort(static (a, b) =>
        {
            int byScore = b.Value.CompareTo(a.Value);
            return byScore != 0 ? byScore : string.CompareOrdinal(a.Slug, b.Slug);
        });

        // Competition ranking: a new rank starts only when the score changes, so equal scores share the
        // rank of their first position and the next distinct score skips the tied positions (1, 1, 3).
        var ranked = new List<RankedSchool>(published.Count);
        int rank = 0;
        for (int index = 0; index < published.Count; index++)
        {
            if (index == 0 || published[index].Value != published[index - 1].Value)
            {
                rank = index + 1;
            }

            ranked.Add(new RankedSchool(rank, published[index].Slug, published[index].Score));
        }

        suppressed.Sort(static (a, b) => string.CompareOrdinal(a.Slug, b.Slug));
        unranked = suppressed;

        return ranked;
    }
}
