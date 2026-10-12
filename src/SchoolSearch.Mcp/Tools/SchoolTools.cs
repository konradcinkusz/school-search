using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SchoolSearch.Mcp.Catalog;

namespace SchoolSearch.Mcp.Tools;

/// <summary>
/// The six read-only school tools (preschool tools are in <see cref="PreschoolTools"/>). Names are part of the public
/// surface and are fixed: no tool name may contain create, update, write or delete. Each tool forwards to one Api
/// endpoint and returns its JSON verbatim.
/// </summary>
[McpServerToolType]
public sealed class SchoolTools(CatalogClient catalog)
{
    internal const int MaxPageSize = 50;
    internal const int MinComparedSchools = 2;
    internal const int MaxComparedSchools = 4;

    [McpServerTool(Name = "find_schools")]
    [Description("Finds primary schools by name, gmina, public status or distance from a point. Sort by name (default) or by score (descending, unscored schools last; there is no ascending option)." + ToolDescriptions.ScoreRules)]
    public async Task<CallToolResult> FindSchools(
        [Description("Case-insensitive part of the school name, at least 2 characters.")] string? query = null,
        [Description("Gmina TERYT code, 7 digits.")] string? gmina = null,
        [Description("true for public schools, false for non-public schools.")] bool? isPublic = null,
        [Description("Latitude of the search centre. Only with lon.")] double? lat = null,
        [Description("Longitude of the search centre. Only with lat.")] double? lon = null,
        [Description("Search radius in km, up to 50.")] double? radiusKm = null,
        [Description("'name' (default) or 'score'.")] string? sort = null,
        [Description("Page number, starting at 1.")] int? page = null,
        [Description("Items per page, 1 to 50.")] int? pageSize = null,
        CancellationToken ct = default)
    {
        if (sort is not null && sort is not ("name" or "score"))
        {
            return ToolResponse.Failure("validation_failed", "sort must be 'name' or 'score'.");
        }

        var radius = radiusKm is null ? (double?)null : Math.Clamp(radiusKm.Value, 0.1, 50);

        var queryString = ApiQuery.Build(
            ("q", query),
            ("gmina", gmina),
            ("isPublic", ApiQuery.Flag(isPublic)),
            ("lat", ApiQuery.Number(lat)),
            ("lon", ApiQuery.Number(lon)),
            ("radiusKm", ApiQuery.Number(radius)),
            ("sort", sort),
            ("page", ApiQuery.Integer(page is null ? null : Math.Max(1, page.Value))),
            ("pageSize", ApiQuery.Integer(ClampPageSize(pageSize))));

        return ToolResponse.From(await catalog.GetAsync("api/v1/schools" + queryString, ct));
    }

    [McpServerTool(Name = "get_school")]
    [Description("Returns one school profile by its RSPO-linked identifier (rspo): governing body type, cohorts, the score trend by exam year with coverage and subject results, and provenance for each field." + ToolDescriptions.ScoreRules)]
    public async Task<CallToolResult> GetSchool(
        [Description("RSPO identifier of the school (positive integer).")] long rspo,
        CancellationToken ct = default)
    {
        if (rspo <= 0)
        {
            return ToolResponse.Failure("validation_failed", "rspo must be a positive integer.");
        }

        return ToolResponse.From(await catalog.GetAsync($"api/v1/schools/by-id/{rspo}", ct));
    }

    [McpServerTool(Name = "get_catchment_school")]
    [Description("Answers which school's catchment (obwód) covers an address, according to the gmina's published resolution. Match 'street_list_match' names the school; 'no_match' means the street is not in the list; 'no_number_match' means the street is listed but no rule covers the house number; 'no_data' means the gmina has no published street list. 'no_data' is NOT a school and must never be turned into a guess from distance. The gmina decides the catchment: always give the resolutionUrl and the disclaimer." + ToolDescriptions.ScoreRules)]
    public async Task<CallToolResult> GetCatchmentSchool(
        [Description("Gmina TERYT code, 7 digits.")] string gmina,
        [Description("Street name, for example 'ul. Przykładowa'.")] string street,
        [Description("House number, for example '12' or '12A'.")] string? number = null,
        CancellationToken ct = default)
    {
        var teryt = gmina?.Trim() ?? string.Empty;
        if (teryt.Length != 7 || !teryt.All(char.IsAsciiDigit))
        {
            return ToolResponse.Failure("validation_failed", "gmina must be a 7-digit TERYT code.");
        }

        if (string.IsNullOrWhiteSpace(street))
        {
            return ToolResponse.Failure("validation_failed", "street is required.");
        }

        var queryString = ApiQuery.Build(
            ("gmina", teryt),
            ("street", street.Trim()),
            ("number", string.IsNullOrWhiteSpace(number) ? null : number.Trim()));

        return ToolResponse.From(await catalog.GetAsync("api/v1/catchment" + queryString, ct));
    }

    [McpServerTool(Name = "compare_schools")]
    [Description("Returns the profiles of 2 to 4 schools side by side, given their slugs (as returned by find_schools)." + ToolDescriptions.ScoreRules)]
    public async Task<CallToolResult> CompareSchools(
        [Description("Two to four school slugs.")] string[] slugs,
        CancellationToken ct = default)
    {
        var cleaned = (slugs ?? []).Select(s => s?.Trim() ?? string.Empty).ToArray();
        if (cleaned.Length is < MinComparedSchools or > MaxComparedSchools || cleaned.Any(s => s.Length == 0))
        {
            return ToolResponse.Failure("validation_failed", "Provide 2 to 4 non-empty school slugs.");
        }

        var joined = string.Join(",", cleaned.Select(Uri.EscapeDataString));
        return ToolResponse.From(await catalog.GetAsync("api/v1/compare?slugs=" + joined, ct));
    }

    [McpServerTool(Name = "get_ranking")]
    [Description("Returns the published ranking for the country or for one gmina, for one exam year. The ranking is descending by score only; there is no sort or direction argument. Schools with a suppressed score are not ranked: unrankedCount counts them and they are never shown as a zero. This is not an official ranking." + ToolDescriptions.ScoreRules)]
    public async Task<CallToolResult> GetRanking(
        [Description("'country' or 'gmina'.")] string scope,
        [Description("Gmina TERYT code, 7 digits. Required when scope is 'gmina'.")] string? gmina = null,
        [Description("Exam year. Omit for the latest published year.")] int? year = null,
        [Description("Page number, starting at 1.")] int? page = null,
        [Description("Items per page, 1 to 50.")] int? pageSize = null,
        CancellationToken ct = default)
    {
        if (scope is not ("country" or "gmina"))
        {
            return ToolResponse.Failure("validation_failed", "scope must be 'country' or 'gmina'.");
        }

        if (scope == "gmina" && string.IsNullOrWhiteSpace(gmina))
        {
            return ToolResponse.Failure("validation_failed", "gmina is required when scope is 'gmina'.");
        }

        var queryString = ApiQuery.Build(
            ("scope", scope),
            ("gmina", string.IsNullOrWhiteSpace(gmina) ? null : gmina.Trim()),
            ("year", ApiQuery.Integer(year)),
            ("page", ApiQuery.Integer(page is null ? null : Math.Max(1, page.Value))),
            ("pageSize", ApiQuery.Integer(ClampPageSize(pageSize))));

        return ToolResponse.From(await catalog.GetAsync("api/v1/ranking" + queryString, ct));
    }

    [McpServerTool(Name = "get_methodology")]
    [Description("Returns the current scoring methodology: version, publication threshold, coverage floor, weights, interval definition, description and the documentation link. Use it to explain what a score means." + ToolDescriptions.ScoreRules)]
    public async Task<CallToolResult> GetMethodology(CancellationToken ct = default) =>
        ToolResponse.From(await catalog.GetAsync("api/v1/methodology", ct));

    private static int? ClampPageSize(int? pageSize) =>
        pageSize is null ? null : Math.Clamp(pageSize.Value, 1, MaxPageSize);
}
