using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.Models;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Api.Endpoints;

/// <summary>Public catalog endpoints under /api/v1. Phase A has no authenticated group (ADR-0004: no JWT wiring yet).</summary>
public static class CatalogEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();

        api.MapGet("/schools", ListSchools).WithName(ApiOperations.ListSchools);
        api.MapGet("/schools/{slug}", GetSchool).WithName(ApiOperations.GetSchool);
        api.MapGet("/meta", GetMeta).WithName(ApiOperations.GetMeta);

        return app;
    }

    /// <summary>Page and limit are clamped (page >= 1, limit in 1..100). Unparseable values fall back to defaults.</summary>
    internal static async Task<IResult> ListSchools(
        string? page,
        string? limit,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (pageNumber, pageSize) = ClampPaging(page, limit);

        var publicSchools = db.Schools.AsNoTracking().Where(s => s.IsPublic);
        var total = await publicSchools.CountAsync(ct);

        var offset = (long)(pageNumber - 1) * pageSize;
        var items = new List<SchoolDto>();
        if (offset < total)
        {
            var rows = await publicSchools
                .OrderBy(s => s.Name)
                .ThenBy(s => s.IdPodmiot)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(ct);

            items = rows.Select(SchoolDto.From).ToList();
        }

        return TypedResults.Ok(new SchoolPage(items, pageNumber, pageSize, total));
    }

    internal static async Task<IResult> GetSchool(
        [AsParameters] SchoolSlugRequest request,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var school = await db.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == request.Slug && s.IsPublic, ct);

        return school is null
            ? ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "School not found.")
            : TypedResults.Ok(SchoolDto.From(school));
    }

    internal static IResult GetMeta() =>
        TypedResults.Ok(new MetaResponse(Methodology.Version, DataVintage: null));

    private static (int Page, int Limit) ClampPaging(string? page, string? limit)
    {
        var pageNumber = int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1;
        var pageSize = int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : DefaultPageSize;

        return (Math.Max(1, pageNumber), Math.Clamp(pageSize, 1, MaxPageSize));
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class ApiOperations
{
    public const string ListSchools = "ListSchools";
    public const string GetSchool = "GetSchool";
    public const string GetMeta = "GetMeta";
}
