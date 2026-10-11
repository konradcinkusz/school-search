using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.Models;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Api.Services;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Search;

/// <summary>
/// Public read API under /api/v1. Every list is clamped, never rejected; a malformed optional parameter falls back to its
/// default. Scores are only produced by the scoring engine (through <see cref="SchoolScoreService"/>). Score ordering is
/// descending with unscored schools last; no other score direction exists (METRIC-ETHICS §1).
/// </summary>
public static class SearchEndpoints
{
    internal const int DefaultPageSize = 20;
    internal const int MaxPageSize = 100;
    internal const int MinQueryLength = 2;
    internal const int MaxComparedSchools = 4;
    internal const int MinComparedSchools = 2;
    internal const double DefaultRadiusKm = 10d;
    internal const double MinRadiusKm = 0.1d;
    internal const double MaxRadiusKm = 50d;
    internal const string MethodologyDocsUrl =
        "https://github.com/konradcinkusz/school-search/blob/main/docs/methodology/RANKING-METHODOLOGY.md";

    private static readonly IReadOnlyDictionary<long, SchoolScore> EmptyScores =
        new Dictionary<long, SchoolScore>();

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();

        api.MapGet("/schools", ListSchools).WithName(SearchOperations.ListSchools);
        api.MapGet("/schools/{slug}", GetSchool).WithName(SearchOperations.GetSchool);
        api.MapGet("/ranking", GetRanking).WithName(SearchOperations.GetRanking);
        api.MapGet("/compare", CompareSchools).WithName(SearchOperations.CompareSchools);
        api.MapGet("/methodology", GetMethodology).WithName(SearchOperations.GetMethodology);
        api.MapGet("/gminas", ListGminas).WithName(SearchOperations.ListGminas);

        return app;
    }

    /// <summary>
    /// Lists schools. Filters: q (name substring, two characters or more), gmina (TERYT), isPublic, and a radius
    /// search (lat, lon, radiusKm up to 50). sort=name (default) or sort=score (descending, unscored last).
    /// </summary>
    internal static async Task<IResult> ListSchools(
        string? q,
        string? gmina,
        string? isPublic,
        string? lat,
        string? lon,
        string? radiusKm,
        string? sort,
        string? page,
        string? pageSize,
        CatalogDbContext db,
        SchoolScoreService scores,
        CancellationToken ct)
    {
        var (pageNumber, size) = ClampPaging(page, pageSize);

        var teryt = Clean(gmina);
        if (teryt is not null && !IsTeryt(teryt))
        {
            return Invalid("Parametr gmina musi być kodem TERYT gminy (7 cyfr).");
        }

        var term = Clean(q);
        if (term is not null && term.Length < MinQueryLength)
        {
            term = null;
        }

        bool? publicOnly = null;
        if (bool.TryParse(Clean(isPublic), out var publicFlag))
        {
            publicOnly = publicFlag;
        }

        var point = ParseGeo(lat, lon, radiusKm);
        var sortByScore = string.Equals(Clean(sort), "score", StringComparison.OrdinalIgnoreCase);

        IQueryable<School> query = db.Schools.AsNoTracking();
        if (term is not null)
        {
            query = SchoolReadModel.WhereNameContains(query, db, term);
        }

        if (teryt is not null)
        {
            query = query.Where(s => s.GminaTeryt == teryt);
        }

        if (publicOnly is bool isPublicValue)
        {
            query = query.Where(s => s.IsPublic == isPublicValue);
        }

        if (point is { } near)
        {
            // Bounding box in SQL, exact haversine distance in memory (see FilterWithinRadius).
            var box = SchoolReadModel.BoundingBox(near.Lat, near.Lon, near.RadiusKm);
            var minLat = box.MinLat;
            var maxLat = box.MaxLat;
            var minLon = box.MinLon;
            var maxLon = box.MaxLon;
            query = query.Where(s => s.Latitude >= minLat && s.Latitude <= maxLat
                && s.Longitude >= minLon && s.Longitude <= maxLon);
        }

        var year = await scores.ResolveCurrentYearAsync(ct);
        SchoolScore? noBaseline = year is null ? SchoolScore.Missing(ScoreStatuses.NoBaseline, null) : null;
        var offset = (long)(pageNumber - 1) * size;
        var distances = new Dictionary<long, double>();

        List<SchoolCandidate> pageRows;
        IReadOnlyDictionary<long, SchoolScore> scoreMap;
        int total;

        if (sortByScore)
        {
            // Score sort needs every matching school scored before the page can be cut.
            var candidates = await query.Select(SchoolReadModel.CandidateProjection).ToListAsync(ct);
            if (point is { } radius)
            {
                candidates = FilterWithinRadius(candidates, radius, distances);
            }

            scoreMap = await ScoreRowsAsync(scores, year, candidates.Select(c => c.Id).ToArray(), ct);
            var ordered = candidates
                .OrderBy(c => ScoreOf(scoreMap, c.Id) is null ? 1 : 0)
                .ThenByDescending(c => ScoreOf(scoreMap, c.Id) ?? 0d)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Slug, StringComparer.Ordinal)
                .ToList();

            total = ordered.Count;
            pageRows = Page(ordered, offset, size);
        }
        else if (point is { } nearby)
        {
            var candidates = await query.Select(SchoolReadModel.CandidateProjection).ToListAsync(ct);
            var inside = FilterWithinRadius(candidates, nearby, distances)
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ThenBy(c => c.Slug, StringComparer.Ordinal)
                .ToList();

            total = inside.Count;
            pageRows = Page(inside, offset, size);
            scoreMap = await ScoreRowsAsync(scores, year, pageRows.Select(c => c.Id).ToArray(), ct);
        }
        else
        {
            total = await query.CountAsync(ct);
            pageRows = offset >= total
                ? new List<SchoolCandidate>()
                : await query
                    .OrderBy(s => s.Name)
                    .ThenBy(s => s.Slug)
                    .Skip((int)offset)
                    .Take(size)
                    .Select(SchoolReadModel.CandidateProjection)
                    .ToListAsync(ct);
            scoreMap = await ScoreRowsAsync(scores, year, pageRows.Select(c => c.Id).ToArray(), ct);
        }

        var items = pageRows
            .Select(c => SchoolReadModel.ToListItem(
                c,
                Lookup(scoreMap, c.Id) ?? noBaseline,
                DistanceOf(distances, c.Id)))
            .ToList();

        return TypedResults.Ok(new SchoolPage(items, pageNumber, size, total));
    }

    internal static async Task<IResult> GetSchool(
        [AsParameters] SchoolSlugRequest request,
        CatalogDbContext db,
        SchoolScoreService scores,
        CancellationToken ct)
    {
        var school = await db.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == request.Slug, ct);
        if (school is null)
        {
            return SchoolNotFound();
        }

        var year = await scores.ResolveCurrentYearAsync(ct);
        var profile = await SchoolReadModel.BuildProfileAsync(db, scores, school, year, ct);
        return TypedResults.Ok(profile);
    }

    /// <summary>Descending ranking of published scores. There is no sort or direction parameter.</summary>
    internal static async Task<IResult> GetRanking(
        string? scope,
        string? gmina,
        string? year,
        string? page,
        string? pageSize,
        SchoolScoreService scores,
        SearchReadService reads,
        CancellationToken ct)
    {
        var scopeName = Clean(scope)?.ToLowerInvariant() ?? "country";
        if (scopeName is not ("country" or "gmina"))
        {
            return Invalid("Parametr scope przyjmuje wartości country albo gmina.");
        }

        string? teryt = null;
        if (scopeName == "gmina")
        {
            teryt = Clean(gmina);
            if (teryt is null || !IsTeryt(teryt))
            {
                return Invalid("Dla scope=gmina podaj parametr gmina: kod TERYT (7 cyfr).");
            }
        }

        var examYear = ParseInt(year) ?? await scores.ResolveCurrentYearAsync(ct);
        var snapshot = await reads.GetRankingAsync(scopeName, teryt, examYear, ct);
        var (pageNumber, size) = ClampPaging(page, pageSize);

        var items = Page(snapshot.Ranked, (long)(pageNumber - 1) * size, size);
        return TypedResults.Ok(new RankingPage(
            scopeName,
            teryt,
            snapshot.ExamYear,
            ScoringPolicy.MethodologyVersion,
            items,
            snapshot.UnrankedCount,
            snapshot.Ranked.Count,
            pageNumber,
            size));
    }

    /// <summary>Side-by-side profiles of two to four distinct schools, in the requested order.</summary>
    internal static async Task<IResult> CompareSchools(
        string? slugs,
        CatalogDbContext db,
        SchoolScoreService scores,
        CancellationToken ct)
    {
        var requested = (slugs ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length < MinComparedSchools || requested.Length > MaxComparedSchools)
        {
            return Invalid("Porównanie wymaga od 2 do 4 różnych szkół (parametr slugs, oddzielony przecinkami).");
        }

        var schools = await db.Schools
            .AsNoTracking()
            .Where(s => requested.Contains(s.Slug))
            .ToListAsync(ct);
        var bySlug = schools.ToDictionary(s => s.Slug, StringComparer.Ordinal);

        var year = await scores.ResolveCurrentYearAsync(ct);
        var profiles = new List<SchoolProfile>(requested.Length);
        foreach (var slug in requested)
        {
            if (!bySlug.TryGetValue(slug, out var school))
            {
                return SchoolNotFound();
            }

            profiles.Add(await SchoolReadModel.BuildProfileAsync(db, scores, school, year, ct));
        }

        return TypedResults.Ok(new ComparePage(profiles));
    }

    internal static IResult GetMethodology() => TypedResults.Ok(new MethodologyInfo(
        ScoringPolicy.MethodologyVersion,
        ScoringPolicy.PublicationThreshold,
        ScoringPolicy.CoverageFloor,
        "equal",
        ScoringPolicy.IntervalZ,
        MethodologyDescription,
        MethodologyDocsUrl));

    internal static async Task<IResult> ListGminas(SearchReadService reads, CancellationToken ct) =>
        TypedResults.Ok(await reads.ListGminasAsync(ct));

    private static readonly string MethodologyDescription =
        "Wynik szkoły pokazuje, o ile punktów procentowych średni wynik szkoły z egzaminu jest wyższy lub niższy "
        + "od średniego wyniku krajowego w tych samych przedmiotach i w tym samym roku. Wynik 0 oznacza średnią krajową. "
        + "Przedmioty mają równe wagi. Gdy do egzaminu przystąpiło mniej niż "
        + $"{(int)Math.Round(ScoringPolicy.CoverageFloor * 100)}% uczniów klasy VIII, wynik jest proporcjonalnie pomniejszany. "
        + $"Wynik podawany jest tylko przy co najmniej {ScoringPolicy.PublicationThreshold} zdających i zawsze z liczbą "
        + "zdających, przedziałem ufności i wersją metodologii. Wynik opisuje szkołę, a nie nauczycieli ani uczniów. "
        + "Nie jest publikacją żadnego organu publicznego.";

    private static async Task<IReadOnlyDictionary<long, SchoolScore>> ScoreRowsAsync(
        SchoolScoreService scores,
        int? year,
        IReadOnlyCollection<long> ids,
        CancellationToken ct)
    {
        if (year is not int scoredYear || ids.Count == 0)
        {
            return EmptyScores;
        }

        return await scores.ScoreAsync(scoredYear, ids, ct);
    }

    private static List<SchoolCandidate> FilterWithinRadius(
        IEnumerable<SchoolCandidate> candidates,
        (double Lat, double Lon, double RadiusKm) point,
        Dictionary<long, double> distances)
    {
        var inside = new List<SchoolCandidate>();
        foreach (var candidate in candidates)
        {
            if (candidate.Latitude is not double lat || candidate.Longitude is not double lon)
            {
                continue;
            }

            var km = SchoolReadModel.HaversineKm(point.Lat, point.Lon, lat, lon);
            if (km > point.RadiusKm)
            {
                continue;
            }

            distances[candidate.Id] = Math.Round(km, 2);
            inside.Add(candidate);
        }

        return inside;
    }

    private static double? ScoreOf(IReadOnlyDictionary<long, SchoolScore> scoreMap, long id) =>
        scoreMap.TryGetValue(id, out var score) ? score.Dto?.Score : null;

    private static SchoolScore? Lookup(IReadOnlyDictionary<long, SchoolScore> scoreMap, long id) =>
        scoreMap.TryGetValue(id, out var score) ? score : null;

    private static double? DistanceOf(Dictionary<long, double> distances, long id) =>
        distances.TryGetValue(id, out var km) ? km : null;

    private static List<T> Page<T>(IReadOnlyList<T> items, long offset, int size)
    {
        if (offset >= items.Count)
        {
            return new List<T>();
        }

        return items.Skip((int)offset).Take(size).ToList();
    }

    private static (double Lat, double Lon, double RadiusKm)? ParseGeo(string? lat, string? lon, string? radiusKm)
    {
        if (!TryParseNumber(lat, -90d, 90d, out var latitude) || !TryParseNumber(lon, -180d, 180d, out var longitude))
        {
            return null;
        }

        var radius = TryParseNumber(radiusKm, double.MinValue, double.MaxValue, out var parsed)
            ? Math.Clamp(parsed, MinRadiusKm, MaxRadiusKm)
            : DefaultRadiusKm;
        return (latitude, longitude, radius);
    }

    private static bool TryParseNumber(string? text, double min, double max, out double value)
    {
        if (double.TryParse(Clean(text), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value)
            && value >= min
            && value <= max)
        {
            return true;
        }

        value = 0d;
        return false;
    }

    /// <summary>Page and pageSize are clamped (page >= 1, pageSize in 1..100). Unparseable values fall back to defaults.</summary>
    private static (int Page, int Size) ClampPaging(string? page, string? pageSize)
    {
        var pageNumber = ParseInt(page) ?? 1;
        var size = ParseInt(pageSize) ?? DefaultPageSize;
        return (Math.Max(1, pageNumber), Math.Clamp(size, 1, MaxPageSize));
    }

    private static int? ParseInt(string? text) =>
        int.TryParse(Clean(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTeryt(string value) => value.Length == 7 && value.All(char.IsAsciiDigit);

    private static IResult Invalid(string message) =>
        ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", message);

    private static IResult SchoolNotFound() =>
        ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Nie znaleziono szkoły.");
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class SearchOperations
{
    public const string ListSchools = "ListSchools";
    public const string GetSchool = "GetSchool";
    public const string GetRanking = "GetRanking";
    public const string CompareSchools = "CompareSchools";
    public const string GetMethodology = "GetMethodology";
    public const string ListGminas = "ListGminas";
}
