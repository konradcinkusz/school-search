using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Preschools;
using SchoolSearch.Api.Search;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Contracts.Public;

namespace SchoolSearch.Api.Map;

/// <summary>Validated parameters of a map markers request.</summary>
internal sealed record MapMarkerQuery(
    double South,
    double West,
    double North,
    double East,
    bool IncludeSchools,
    bool IncludePreschools,
    string? Term,
    string? Gmina,
    bool PublicOnly,
    decimal? MaxTuitionPln,
    bool HasFreePlaces,
    int Limit);

/// <summary>
/// Reads markers for the map from the catalog database. Only location, name, kind, public flag, gmina name and the
/// preschool facts (tuition range, free places) are selected: no scoring, ranking, review or billing data is touched.
/// Bounding box filters are plain comparisons on the latitude and longitude columns.
/// </summary>
internal sealed class MapMarkerService(CatalogDbContext db)
{
    public async Task<MapMarkersResponse> GetMarkersAsync(MapMarkerQuery request, CancellationToken ct)
    {
        var markers = new List<MapMarker>();
        var total = 0;

        if (request.IncludeSchools)
        {
            var schools = SchoolQuery(request);
            total += await schools.CountAsync(ct);
            var rows = await schools
                .OrderBy(s => s.Slug)
                .Take(request.Limit)
                .Select(s => new SchoolRow(s.Slug, s.Name, s.Latitude, s.Longitude, s.IsPublic, s.GminaName))
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                markers.Add(new MapMarker(
                    "school", row.Slug, row.Name, row.Latitude ?? 0d, row.Longitude ?? 0d, row.IsPublic, row.GminaName, null, null));
            }
        }

        if (request.IncludePreschools)
        {
            var preschools = PreschoolQuery(request);
            total += await preschools.CountAsync(ct);
            var rows = await preschools
                .OrderBy(p => p.Slug)
                .Take(request.Limit)
                .Select(p => new PreschoolRow(
                    p.Slug,
                    p.Name,
                    p.Latitude,
                    p.Longitude,
                    p.IsPublic,
                    p.GminaName,
                    p.Facts == null ? null : p.Facts.MonthlyTuitionMinPln,
                    p.Facts == null ? null : p.Facts.MonthlyTuitionMaxPln,
                    p.Facts == null ? null : p.Facts.FreePlaces))
                .ToListAsync(ct);
            foreach (var row in rows)
            {
                markers.Add(new MapMarker(
                    "preschool",
                    row.Slug,
                    row.Name,
                    row.Latitude ?? 0d,
                    row.Longitude ?? 0d,
                    row.IsPublic,
                    row.GminaName,
                    new MoneyRange(row.TuitionMin, row.TuitionMax),
                    row.FreePlaces));
            }
        }

        // Each kind was cut at the limit in slug order, so the first <limit> of the merge are all present.
        var items = markers
            .OrderBy(m => m.Slug, StringComparer.Ordinal)
            .ThenBy(m => m.Kind, StringComparer.Ordinal)
            .Take(request.Limit)
            .ToList();

        return new MapMarkersResponse(items, total, total > request.Limit, request.Limit);
    }

    private IQueryable<School> SchoolQuery(MapMarkerQuery request)
    {
        var south = request.South;
        var north = request.North;
        var west = request.West;
        var east = request.East;

        IQueryable<School> query = db.Schools.AsNoTracking()
            .Where(s => s.Latitude >= south && s.Latitude <= north && s.Longitude >= west && s.Longitude <= east);

        if (request.Term is string term)
        {
            query = SchoolReadModel.WhereNameContains(query, db, term);
        }

        if (request.Gmina is string teryt)
        {
            query = query.Where(s => s.GminaTeryt == teryt);
        }

        if (request.PublicOnly)
        {
            query = query.Where(s => s.IsPublic);
        }

        return query;
    }

    private IQueryable<Preschool> PreschoolQuery(MapMarkerQuery request)
    {
        var south = request.South;
        var north = request.North;
        var west = request.West;
        var east = request.East;

        IQueryable<Preschool> query = db.Preschools.AsNoTracking()
            .Where(p => p.Latitude >= south && p.Latitude <= north && p.Longitude >= west && p.Longitude <= east);

        if (request.Term is string term)
        {
            query = PreschoolReadModel.WhereNameContains(query, db, term);
        }

        if (request.Gmina is string teryt)
        {
            query = query.Where(p => p.GminaTeryt == teryt);
        }

        if (request.PublicOnly)
        {
            query = query.Where(p => p.IsPublic);
        }

        if (request.MaxTuitionPln is decimal ceiling)
        {
            // Unknown tuition never matches a tuition ceiling (as in the preschool list).
            query = query.Where(p => p.Facts != null && p.Facts.MonthlyTuitionMinPln <= ceiling);
        }

        if (request.HasFreePlaces)
        {
            query = query.Where(p => p.Facts != null && p.Facts.FreePlaces > 0);
        }

        return query;
    }

    private sealed record SchoolRow(string Slug, string Name, double? Latitude, double? Longitude, bool IsPublic, string? GminaName);

    private sealed record PreschoolRow(
        string Slug,
        string Name,
        double? Latitude,
        double? Longitude,
        bool IsPublic,
        string? GminaName,
        decimal? TuitionMin,
        decimal? TuitionMax,
        int? FreePlaces);
}
