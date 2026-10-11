using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.Models;

namespace SchoolSearch.Api.Endpoints;

/// <summary>
/// Catalog metadata under /api/v1. School listings, profiles, rankings and the rest of the public read surface live in
/// <see cref="SchoolSearch.Api.Search.SearchEndpoints"/>.
/// </summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();

        api.MapGet("/meta", GetMeta).WithName(ApiOperations.GetMeta);

        return app;
    }

    internal static IResult GetMeta() =>
        TypedResults.Ok(new MetaResponse(Methodology.Version, DataVintage: null));
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class ApiOperations
{
    public const string GetMeta = "GetMeta";
}
