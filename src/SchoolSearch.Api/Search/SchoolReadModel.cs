using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Api.Services;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Search;

/// <summary>The columns a school list needs. Projected in SQL so lists never load whole rows.</summary>
public sealed record SchoolCandidate(
    long Id,
    string Slug,
    string Name,
    string? GminaTeryt,
    string? GminaName,
    bool IsPublic,
    bool IsSpecial,
    double? Latitude,
    double? Longitude);

/// <summary>Mapping from stored rows to public DTOs, plus the query helpers shared by the search endpoints.</summary>
internal static class SchoolReadModel
{
    /// <summary>Note shown when the faith flag was derived from the governing body type (Polish, user-facing).</summary>
    public const string FaithDerivedNote = "Oznaczenie wyprowadzone z typu organu prowadzącego; szkoła może je skorygować.";

    private const string FaithDeclaredNote = "Oznaczenie podane w źródle danych.";
    private const string FaithAbsentNote = "Brak oznaczenia wyznaniowego w danych.";
    private const double EarthRadiusKm = 6371.0088;
    private const double KmPerDegree = 110.574;

    public static readonly Expression<Func<School, SchoolCandidate>> CandidateProjection = s => new SchoolCandidate(
        s.IdPodmiot,
        s.Slug,
        s.Name,
        s.GminaTeryt,
        s.GminaName,
        s.IsPublic,
        s.IsSpecial,
        s.Latitude,
        s.Longitude);

    public static SchoolListItem ToListItem(SchoolCandidate candidate, SchoolScore? score, double? distanceKm) => new(
        candidate.Slug,
        candidate.Name,
        candidate.GminaTeryt,
        candidate.GminaName,
        candidate.IsPublic,
        candidate.IsSpecial,
        candidate.Latitude,
        candidate.Longitude,
        distanceKm,
        score?.ScoreStatus ?? ScoreStatuses.NoExamData,
        SchoolScoreService.ToBlock(score));

    /// <summary>
    /// Full profile of one school. The current score is the trend point of <paramref name="currentYear"/>, so the
    /// profile and its history can never disagree. Contains no operator name: the governing body is a type only.
    /// </summary>
    public static async Task<SchoolProfile> BuildProfileAsync(
        CatalogDbContext db,
        SchoolScoreService scores,
        School school,
        int? currentYear,
        CancellationToken ct)
    {
        var trend = await scores.TrendAsync(school.IdPodmiot, ct);

        string scoreStatus;
        ScoreBlock? scoreBlock = null;
        if (currentYear is not int year)
        {
            scoreStatus = ScoreStatuses.NoBaseline;
        }
        else
        {
            var current = trend.FirstOrDefault(p => p.ExamYear == year);
            scoreStatus = current?.ScoreStatus ?? ScoreStatuses.NoExamData;
            scoreBlock = current?.Score;
        }

        GoverningBodyType? bodyType = null;
        if (school.GoverningBodyTypeCode is int bodyCode)
        {
            bodyType = await db.GoverningBodyTypes.AsNoTracking()
                .FirstOrDefaultAsync(g => g.Code == bodyCode, ct);
        }

        var governingBody = new GoverningBodyInfo(
            school.GoverningBodyTypeCode,
            bodyType?.Label,
            bodyType?.LabelVerified ?? false,
            bodyType?.IsNaturalPerson ?? false);

        var faithNote = school.FaithAffiliationDerived
            ? FaithDerivedNote
            : school.FaithAffiliationDeclared == true ? FaithDeclaredNote : FaithAbsentNote;
        var faith = new FaithInfo(school.FaithAffiliationDerived, school.FaithAffiliationDeclared, faithNote);

        var cohortRows = await db.ClassCohorts.AsNoTracking()
            .Where(c => c.SchoolId == school.IdPodmiot)
            .OrderBy(c => c.SchoolYear)
            .ThenBy(c => c.ClassLevel)
            .ToListAsync(ct);
        var cohorts = cohortRows
            .Select(c => new CohortRow(c.SchoolYear, c.ClassLevel, c.Pupils, c.Oddzialy))
            .ToList();

        var examProvenance = await db.ExamResults.AsNoTracking()
            .Where(e => e.SchoolId == school.IdPodmiot)
            .Select(e => new { e.Provenance.Source, e.Provenance.SourceUrl, e.Provenance.AsOf, e.Provenance.Method })
            .ToListAsync(ct);

        var provenance = new List<ProvenanceRow>();
        provenance.AddRange(ProvenanceRowsFor("name", [(school.Provenance.Source, school.Provenance.SourceUrl, school.Provenance.AsOf, school.Provenance.Method)]));
        provenance.AddRange(ProvenanceRowsFor("gmina", [(school.Provenance.Source, school.Provenance.SourceUrl, school.Provenance.AsOf, school.Provenance.Method)]));
        if (school.CoordinatesProvenance is { } coordinates)
        {
            provenance.AddRange(ProvenanceRowsFor("coordinates", [(coordinates.Source, coordinates.SourceUrl, coordinates.AsOf, coordinates.Method)]));
        }

        provenance.AddRange(ProvenanceRowsFor(
            "cohorts",
            cohortRows.Select(c => (c.Provenance.Source, c.Provenance.SourceUrl, c.Provenance.AsOf, c.Provenance.Method))));
        provenance.AddRange(ProvenanceRowsFor(
            "examResults",
            examProvenance.Select(e => (e.Source, e.SourceUrl, e.AsOf, e.Method))));

        return new SchoolProfile(
            school.Slug,
            school.Name,
            school.GminaTeryt,
            school.GminaName,
            school.IsPublic,
            school.IsSpecial,
            school.Latitude,
            school.Longitude,
            DistanceKm: null,
            scoreStatus,
            scoreBlock,
            governingBody,
            faith,
            cohorts,
            trend,
            provenance,
            ScoringPolicy.MethodologyVersion);
    }

    /// <summary>
    /// Case-insensitive substring match on the name. Npgsql uses ILIKE; other providers (InMemory in tests) use a
    /// lower-cased Contains. LIKE wildcards in the term are escaped, so the term is always a literal substring.
    /// </summary>
    public static IQueryable<School> WhereNameContains(IQueryable<School> query, CatalogDbContext db, string term)
    {
        if (db.Database.IsRelational())
        {
            var pattern = "%" + EscapeLike(term) + "%";
            return query.Where(s => EF.Functions.ILike(s.Name, pattern));
        }

        var needle = term.ToLowerInvariant();
        return query.Where(s => s.Name.ToLower().Contains(needle));
    }

    /// <summary>
    /// Bounding box that always contains the circle of <paramref name="radiusKm"/> around the point. The box is applied
    /// in SQL; the exact distance is then checked in memory with <see cref="HaversineKm"/>.
    /// </summary>
    public static (double MinLat, double MaxLat, double MinLon, double MaxLon) BoundingBox(double lat, double lon, double radiusKm)
    {
        double deltaLat = radiusKm / KmPerDegree;
        double cosLat = Math.Max(0.01d, Math.Cos(ToRadians(lat)));
        double deltaLon = radiusKm / (KmPerDegree * cosLat);
        return (Math.Max(-90d, lat - deltaLat), Math.Min(90d, lat + deltaLat), lon - deltaLon, lon + deltaLon);
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2d * EarthRadiusKm * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => Math.PI / 180d * degrees;

    private static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static IEnumerable<ProvenanceRow> ProvenanceRowsFor(
        string field,
        IEnumerable<(string Source, string SourceUrl, DateOnly AsOf, string Method)> values) =>
        values
            .Distinct()
            .OrderByDescending(v => v.AsOf)
            .ThenBy(v => v.Source, StringComparer.Ordinal)
            .Select(v => new ProvenanceRow(
                field,
                v.Source,
                v.SourceUrl,
                v.AsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                v.Method));
}
