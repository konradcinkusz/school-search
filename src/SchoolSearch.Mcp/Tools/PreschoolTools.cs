using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SchoolSearch.Mcp.Catalog;

namespace SchoolSearch.Mcp.Tools;

/// <summary>
/// The two read-only preschool tools (przedszkola). Each forwards to one Api endpoint and returns its JSON verbatim.
/// Preschools have no quality score (ADR-0014), so no argument sorts or filters by one.
/// </summary>
[McpServerToolType]
public sealed class PreschoolTools(CatalogClient catalog)
{
    private const double MinRadiusKm = 0.1;
    private const double MaxRadiusKm = 50;

    [McpServerTool(Name = "find_preschools")]
    [Description("Finds preschools by name, gmina, public status, monthly tuition, free places or distance from a point. Results are sorted by name, ascending. There is no sort argument." + ToolDescriptions.PreschoolRules)]
    public async Task<CallToolResult> FindPreschools(
        [Description("Case-insensitive part of the preschool name, at least 2 characters.")] string? q = null,
        [Description("Gmina TERYT code, 7 digits.")] string? gmina = null,
        [Description("true for public preschools only, false for non-public preschools only.")] bool? publicOnly = null,
        [Description("Highest monthly tuition in PLN (czesne), zero or more.")] double? maxTuition = null,
        [Description("true for preschools that report free places. A preschool with an unknown number of free places is not a match.")] bool? hasFreePlaces = null,
        [Description("Latitude of the search centre. Only with lon.")] double? lat = null,
        [Description("Longitude of the search centre. Only with lat.")] double? lon = null,
        [Description("Search radius in km, up to 50.")] double? radiusKm = null,
        [Description("Page number, starting at 1.")] int? page = null,
        [Description("Items per page, 1 to 50.")] int? pageSize = null,
        CancellationToken ct = default)
    {
        if (!AllFinite(lat, lon, radiusKm, maxTuition))
        {
            return ToolResponse.Failure("validation_failed", "lat, lon, radiusKm and maxTuition must be finite numbers.");
        }

        if (maxTuition < 0)
        {
            return ToolResponse.Failure("validation_failed", "maxTuition must be zero or more.");
        }

        if (gmina is not null && !IsTeryt(gmina.Trim()))
        {
            return ToolResponse.Failure("validation_failed", "gmina must be a 7-digit TERYT code.");
        }

        var radius = radiusKm is null ? (double?)null : Math.Clamp(radiusKm.Value, MinRadiusKm, MaxRadiusKm);

        var queryString = ApiQuery.Build(
            ("q", string.IsNullOrWhiteSpace(q) ? null : q.Trim()),
            ("gmina", string.IsNullOrWhiteSpace(gmina) ? null : gmina.Trim()),
            ("publicOnly", ApiQuery.Flag(publicOnly)),
            ("maxTuition", ApiQuery.Number(maxTuition)),
            ("hasFreePlaces", ApiQuery.Flag(hasFreePlaces)),
            ("lat", ApiQuery.Number(lat)),
            ("lon", ApiQuery.Number(lon)),
            ("radiusKm", ApiQuery.Number(radius)),
            ("page", ApiQuery.Integer(page is null ? null : Math.Max(1, page.Value))),
            ("pageSize", ApiQuery.Integer(ClampPageSize(pageSize))));

        return ToolResponse.From(await catalog.GetAsync("api/v1/preschools" + queryString, ct));
    }

    [McpServerTool(Name = "get_preschool")]
    [Description("Returns one preschool profile by its slug (as returned by find_preschools): address, public or non-public, governing body type, monthly tuition range, opening hours and free places, each with its source, asOf date and confidence. A null facts block means no facts are known yet." + ToolDescriptions.PreschoolRules)]
    public async Task<CallToolResult> GetPreschool(
        [Description("Preschool slug, as returned by find_preschools.")] string slug,
        CancellationToken ct = default)
    {
        var cleaned = slug?.Trim() ?? string.Empty;
        if (cleaned.Length == 0)
        {
            return ToolResponse.Failure("validation_failed", "slug is required.");
        }

        return ToolResponse.From(await catalog.GetAsync("api/v1/preschools/" + Uri.EscapeDataString(cleaned), ct));
    }

    private static bool AllFinite(params double?[] values) =>
        values.All(v => v is null || double.IsFinite(v.Value));

    private static bool IsTeryt(string value) =>
        value.Length == 7 && value.All(char.IsAsciiDigit);

    private static int? ClampPageSize(int? pageSize) =>
        pageSize is null ? null : Math.Clamp(pageSize.Value, 1, SchoolTools.MaxPageSize);
}
