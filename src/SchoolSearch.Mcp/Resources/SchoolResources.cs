using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolSearch.Contracts.Public;
using SchoolSearch.Mcp.Catalog;
using SchoolSearch.Mcp.Tools;

namespace SchoolSearch.Mcp.Resources;

/// <summary>
/// Read-only resources. <c>school://{rspo}</c> is the profile JSON; <c>methodology://{version}</c> resolves only for the
/// current methodology version. Anything else is "not found". Bodies are the Api JSON, verbatim.
/// </summary>
[McpServerResourceType]
public sealed class SchoolResources(CatalogClient catalog)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerResource(UriTemplate = "school://{rspo}", Name = "school_profile", MimeType = "application/json")]
    [Description("School profile by RSPO-linked identifier: score trend with intervals, cohort sizes, coverage and provenance.")]
    public async Task<string> ReadSchool(string rspo, CancellationToken ct)
    {
        if (!long.TryParse(rspo, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
        {
            throw new McpException("Resource not found: school://" + rspo);
        }

        var response = await catalog.GetAsync($"api/v1/schools/by-id/{id}", ct);
        if (response.StatusCode == 404)
        {
            throw new McpException("Resource not found: school://" + rspo);
        }

        if (!response.IsSuccess)
        {
            throw new McpException("The school catalog is temporarily unavailable. Try again shortly.");
        }

        return response.Body;
    }

    [McpServerResource(UriTemplate = "methodology://{version}", Name = "methodology", MimeType = "application/json")]
    [Description("Scoring methodology for one version. Only the current version resolves; others are not found.")]
    public async Task<string> ReadMethodology(string version, CancellationToken ct)
    {
        var response = await catalog.GetAsync("api/v1/methodology", ct);
        if (!response.IsSuccess)
        {
            throw new McpException("The school catalog is temporarily unavailable. Try again shortly.");
        }

        var current = JsonSerializer.Deserialize<MethodologyInfo>(response.Body, JsonOptions);
        if (current is null || !string.Equals(current.Version, version, StringComparison.Ordinal))
        {
            throw new McpException("Resource not found: methodology://" + version);
        }

        return response.Body;
    }
}
