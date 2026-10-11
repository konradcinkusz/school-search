using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Api.Services;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Contracts;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Search;

/// <summary>A ranking over one scope and year: the published schools in rank order, plus the unranked count.</summary>
public sealed record RankingSnapshot(int? ExamYear, IReadOnlyList<RankedItem> Ranked, int UnrankedCount);

/// <summary>
/// Read side of the ranking and gmina listings. Rankings and gmina lists are cached in the shared memory cache for
/// ten minutes per key, so a page request does not re-score 14k schools.
/// </summary>
public sealed class SearchReadService(CatalogDbContext db, SchoolScoreService scores, IMemoryCache cache)
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<RankingSnapshot> GetRankingAsync(string scope, string? gminaTeryt, int? examYear, CancellationToken ct)
    {
        var year = examYear?.ToString(CultureInfo.InvariantCulture) ?? "none";
        var key = $"ranking:{scope}:{gminaTeryt ?? "-"}:{year}";

        var snapshot = await cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1; // the shared cache has a SizeLimit (correction flood guard); every entry must declare a size
            return BuildRankingAsync(scope, gminaTeryt, examYear, ct);
        });
        return snapshot!;
    }

    public async Task<IReadOnlyList<GminaInfo>> ListGminasAsync(CancellationToken ct)
    {
        var gminas = await cache.GetOrCreateAsync("gminas", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheLifetime;
            entry.Size = 1; // the shared cache has a SizeLimit (correction flood guard); every entry must declare a size
            return BuildGminasAsync(ct);
        });
        return gminas!;
    }

    private async Task<RankingSnapshot> BuildRankingAsync(string scope, string? gminaTeryt, int? examYear, CancellationToken ct)
    {
        var schoolQuery = db.Schools.AsNoTracking();
        if (gminaTeryt is not null)
        {
            schoolQuery = schoolQuery.Where(s => s.GminaTeryt == gminaTeryt);
        }

        var schools = await schoolQuery.Select(SchoolReadModel.CandidateProjection).ToListAsync(ct);
        if (examYear is not int year)
        {
            // Without a baseline there is no methodology output to rank: every school is unranked.
            return new RankingSnapshot(null, Array.Empty<RankedItem>(), schools.Count);
        }

        var scopedIds = gminaTeryt is null ? null : schools.Select(s => s.Id).ToArray();
        var scoreMap = await scores.ScoreAsync(year, scopedIds, ct);

        var published = new List<(string Slug, ScoreDto Score)>();
        var scoreBySlug = new Dictionary<string, SchoolScore>(StringComparer.Ordinal);
        var schoolBySlug = new Dictionary<string, SchoolCandidate>(StringComparer.Ordinal);
        foreach (var school in schools)
        {
            schoolBySlug[school.Slug] = school;
            if (scoreMap.TryGetValue(school.Id, out var score) && score.Dto is { } dto)
            {
                // Suppressed scores reach RankingBuilder with a null value and land in the unranked count.
                published.Add((school.Slug, dto));
                scoreBySlug[school.Slug] = score;
            }
        }

        var ranked = RankingBuilder.RankDescending(published, out _);
        var items = ranked
            .Select(r =>
            {
                var school = schoolBySlug[r.Slug];
                var block = SchoolScoreService.ToBlock(scoreBySlug[r.Slug])
                    ?? throw new InvalidOperationException("A ranked school must carry a complete score block.");
                return new RankedItem(r.Rank, r.Slug, school.Name, school.GminaName, block);
            })
            .ToList();

        return new RankingSnapshot(year, items, schools.Count - items.Count);
    }

    private async Task<IReadOnlyList<GminaInfo>> BuildGminasAsync(CancellationToken ct)
    {
        var rows = await db.Schools.AsNoTracking()
            .Where(s => s.GminaTeryt != null)
            .Select(s => new { Teryt = s.GminaTeryt!, Name = s.GminaName })
            .ToListAsync(ct);

        var coverage = await db.CatchmentCoverages.AsNoTracking()
            .Select(c => new { c.GminaTeryt, c.State })
            .ToListAsync(ct);
        var stateByTeryt = coverage.ToDictionary(c => c.GminaTeryt, c => c.State, StringComparer.Ordinal);

        return rows
            .GroupBy(r => r.Teryt, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new GminaInfo(
                g.Key,
                g.Select(r => r.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .FirstOrDefault(),
                g.Count(),
                stateByTeryt.TryGetValue(g.Key, out var state) ? state : "none"))
            .ToList();
    }
}
