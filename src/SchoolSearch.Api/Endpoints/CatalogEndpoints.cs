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

    /// <summary>Page and pageSize are clamped (page >= 1, pageSize in 1..100). Unparseable values fall back to defaults.</summary>
    internal static async Task<IResult> ListSchools(
        string? page,
        string? pageSize,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (pageNumber, size) = ClampPaging(page, pageSize);

        var allSchools = db.Schools.AsNoTracking();
        var total = await allSchools.CountAsync(ct);

        var offset = (long)(pageNumber - 1) * size;
        var items = new List<SchoolDto>();
        if (offset < total)
        {
            var rows = await allSchools
                .OrderBy(s => s.Name)
                .ThenBy(s => s.IdPodmiot)
                .Skip((int)offset)
                .Take(size)
                .ToListAsync(ct);

            items = rows.Select(SchoolDto.From).ToList();
        }

        return TypedResults.Ok(new SchoolPage(items, pageNumber, size, total));
    }

    internal static async Task<IResult> GetSchool(
        [AsParameters] SchoolSlugRequest request,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var school = await db.Schools
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Slug == request.Slug, ct);

        return school is null
            ? ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "School not found.")
            : TypedResults.Ok(SchoolDto.From(school));
    }

    internal static IResult GetMeta() =>
        TypedResults.Ok(new MetaResponse(Methodology.Version, DataVintage: null));

    private static (int Page, int Size) ClampPaging(string? page, string? pageSize)
    {
        var pageNumber = int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1;
        var size = int.TryParse(pageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : DefaultPageSize;

        return (Math.Max(1, pageNumber), Math.Clamp(size, 1, MaxPageSize));
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class ApiOperations
{
    public const string ListSchools = "ListSchools";
    public const string GetSchool = "GetSchool";
    public const string GetMeta = "GetMeta";
}
