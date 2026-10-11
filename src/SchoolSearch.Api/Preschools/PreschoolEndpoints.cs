using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.Models;
using SchoolSearch.Api.Search;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Preschools;

/// <summary>
/// Public read API for preschools under /api/v1. Lists are ordered by name only (alphabetical, never by quality); there
/// is no sort or direction parameter. Malformed optional filters are refused with 400 validation_failed (gmina, lat,
/// lon, radiusKm, maxTuition); unparseable flags are ignored. Non-public preschools are listed, as schools are.
/// </summary>
public static class PreschoolEndpoints
{
    public static IEndpointRouteBuilder MapPreschoolEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();

        api.MapGet("/preschools", ListPreschools).WithName(PreschoolOperations.ListPreschools);
        api.MapGet("/preschools/{slug}", GetPreschool).WithName(PreschoolOperations.GetPreschool);

        return app;
    }

    /// <summary>
    /// Lists preschools. Filters: q (name substring, two characters or more), gmina (TERYT), lat/lon with radiusKm
    /// (default 10, up to 50), publicOnly, maxTuition (monthly tuition minimum at most this amount, PLN), hasFreePlaces.
    /// </summary>
    internal static async Task<IResult> ListPreschools(
        string? q,
        string? gmina,
        string? lat,
        string? lon,
        string? radiusKm,
        string? publicOnly,
        string? maxTuition,
        string? hasFreePlaces,
        string? page,
        string? pageSize,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (pageNumber, size) = ClampPaging(page, pageSize);

        var teryt = Clean(gmina);
        if (teryt is not null && !IsTeryt(teryt))
        {
            return Invalid("Parametr gmina musi być kodem TERYT gminy (7 cyfr).");
        }

        var term = Clean(q);
        if (term is not null && term.Length < SearchEndpoints.MinQueryLength)
        {
            term = null;
        }

        decimal? ceiling = null;
        var maxText = Clean(maxTuition);
        if (maxText is not null)
        {
            if (!decimal.TryParse(maxText.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                || parsed < 0m)
            {
                return Invalid("Parametr maxTuition musi być kwotą w złotych, liczbą nieujemną.");
            }

            ceiling = parsed;
        }

        var latText = Clean(lat);
        var lonText = Clean(lon);
        var radiusText = Clean(radiusKm);

        double? radiusValue = null;
        if (radiusText is not null)
        {
            if (!TryParseDouble(radiusText, SearchEndpoints.MinRadiusKm, SearchEndpoints.MaxRadiusKm, out var parsedRadius))
            {
                return Invalid("Parametr radiusKm musi być liczbą od 0,1 do 50 (km).");
            }

            radiusValue = parsedRadius;
        }

        (double Lat, double Lon, double RadiusKm)? point = null;
        if (latText is not null || lonText is not null)
        {
            if (!TryParseDouble(latText, -90d, 90d, out var latitude) || !TryParseDouble(lonText, -180d, 180d, out var longitude))
            {
                return Invalid("Parametry lat i lon muszą być współrzędnymi: lat od -90 do 90, lon od -180 do 180.");
            }

            point = (latitude, longitude, radiusValue ?? SearchEndpoints.DefaultRadiusKm);
        }

        var publicFlag = ParseBool(publicOnly);
        var freeFlag = ParseBool(hasFreePlaces);

        IQueryable<Preschool> query = db.Preschools.AsNoTracking();
        if (term is not null)
        {
            query = PreschoolReadModel.WhereNameContains(query, db, term);
        }

        if (teryt is not null)
        {
            query = query.Where(p => p.GminaTeryt == teryt);
        }

        if (publicFlag == true)
        {
            query = query.Where(p => p.IsPublic);
        }

        if (ceiling is decimal maxPln)
        {
            // Unknown tuition never matches a tuition ceiling.
            query = query.Where(p => p.Facts != null && p.Facts.MonthlyTuitionMinPln <= maxPln);
        }

        if (freeFlag is bool wantFree)
        {
            query = wantFree
                ? query.Where(p => p.Facts != null && p.Facts.FreePlaces > 0)
                : query.Where(p => p.Facts != null && p.Facts.FreePlaces == 0);
        }

        var offset = (long)(pageNumber - 1) * size;

        if (point is null)
        {
            var total = await query.CountAsync(ct);
            var rows = offset >= total
                ? new List<Preschool>()
                : await query
                    .Include(p => p.Facts)
                    .OrderBy(p => p.Name)
                    .ThenBy(p => p.Slug)
                    .Skip((int)offset)
                    .Take(size)
                    .ToListAsync(ct);

            var plainItems = rows.Select(p => PreschoolReadModel.ToListItem(p, null)).ToList();
            return TypedResults.Ok(new PreschoolPage(plainItems, total, pageNumber, size));
        }

        // Radius search: bounding box in SQL, exact haversine distance in memory (as for schools).
        var center = point.Value;
        var box = SchoolReadModel.BoundingBox(center.Lat, center.Lon, center.RadiusKm);
        var minLat = box.MinLat;
        var maxLat = box.MaxLat;
        var minLon = box.MinLon;
        var maxLon = box.MaxLon;
        var candidates = await query
            .Where(p => p.Latitude >= minLat && p.Latitude <= maxLat && p.Longitude >= minLon && p.Longitude <= maxLon)
            .Include(p => p.Facts)
            .ToListAsync(ct);

        var distances = new Dictionary<long, double>();
        var inside = new List<Preschool>();
        foreach (var candidate in candidates)
        {
            if (candidate.Latitude is not double rowLat || candidate.Longitude is not double rowLon)
            {
                continue;
            }

            var km = SchoolReadModel.HaversineKm(center.Lat, center.Lon, rowLat, rowLon);
            if (km > center.RadiusKm)
            {
                continue;
            }

            distances[candidate.IdPodmiot] = Math.Round(km, 2);
            inside.Add(candidate);
        }

        var ordered = inside
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => p.Slug, StringComparer.Ordinal)
            .ToList();

        var nearbyItems = Page(ordered, offset, size)
            .Select(p => PreschoolReadModel.ToListItem(p, distances[p.IdPodmiot]))
            .ToList();
        return TypedResults.Ok(new PreschoolPage(nearbyItems, ordered.Count, pageNumber, size));
    }

    /// <summary>Profile of one preschool. Unknown slugs answer with the uniform 404.</summary>
    internal static async Task<IResult> GetPreschool(
        [AsParameters] SchoolSlugRequest request,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var preschool = await db.Preschools
            .AsNoTracking()
            .Include(p => p.Facts)
            .FirstOrDefaultAsync(p => p.Slug == request.Slug, ct);
        if (preschool is null)
        {
            return PreschoolNotFound();
        }

        var profile = await PreschoolReadModel.BuildProfileAsync(db, preschool, ct);
        return TypedResults.Ok(profile);
    }

    private static List<T> Page<T>(IReadOnlyList<T> items, long offset, int size)
    {
        if (offset >= items.Count)
        {
            return new List<T>();
        }

        return items.Skip((int)offset).Take(size).ToList();
    }

    private static bool TryParseDouble(string? text, double min, double max, out double value)
    {
        if (text is not null
            && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value)
            && value >= min
            && value <= max)
        {
            return true;
        }

        value = 0d;
        return false;
    }

    private static bool? ParseBool(string? text)
    {
        if (bool.TryParse(Clean(text), out var value))
        {
            return value;
        }

        return null;
    }

    /// <summary>Page and pageSize are clamped (page >= 1, pageSize in 1..100). Unparseable values fall back to defaults.</summary>
    private static (int Page, int Size) ClampPaging(string? page, string? pageSize)
    {
        var pageNumber = ParseInt(page) ?? 1;
        var size = ParseInt(pageSize) ?? SearchEndpoints.DefaultPageSize;
        return (Math.Max(1, pageNumber), Math.Clamp(size, 1, SearchEndpoints.MaxPageSize));
    }

    private static int? ParseInt(string? text) =>
        int.TryParse(Clean(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTeryt(string value) => value.Length == 7 && value.All(char.IsAsciiDigit);

    private static IResult Invalid(string message) =>
        ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", message);

    private static IResult PreschoolNotFound() =>
        ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Nie znaleziono przedszkola.");
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class PreschoolOperations
{
    public const string ListPreschools = "ListPreschools";
    public const string GetPreschool = "GetPreschool";
}
