using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SchoolSearch.Api.Tests;

public sealed class MetaEndpointTests : IDisposable
{
    private readonly SchoolSearchApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Meta_reports_the_draft_methodology_version_and_no_data_vintage()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/meta");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal("0.0-draft", root.GetProperty("methodologyVersion").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("dataVintage").ValueKind);
    }
}

/// <summary>
/// Anti-goal from docs/MASTER-PROMPT.md: "No ascending sort on composite". The ranking
/// endpoint must not expose any sort, order or direction parameter that could request it.
/// Checked against the OpenAPI document the Api publishes, so the contract is what is tested.
/// </summary>
public sealed class RankingContractTests : IDisposable
{
    private readonly SchoolSearchApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Schools_endpoint_offers_no_ascending_composite_sort_parameter()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = await JsonTestHelpers.ReadJsonAsync(response);
        var get = document.GetProperty("paths").GetProperty("/api/v1/schools").GetProperty("get");

        var parameters = get.TryGetProperty("parameters", out var parameterArray)
            ? parameterArray.EnumerateArray().ToList()
            : new List<JsonElement>();

        // Sanity: the endpoint is documented with its paging parameters, so this is the real operation.
        Assert.Contains(parameters, p => p.GetProperty("name").GetString() == "page");

        var sortLikeNames = parameters
            .Select(p => p.GetProperty("name").GetString() ?? string.Empty)
            .Where(name => name.Contains("sort", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("order", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("direction", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Empty(sortLikeNames);

        // Defence in depth: no enum anywhere on the operation may offer an ascending value.
        var operationText = get.GetRawText();
        Assert.DoesNotContain("composite_asc", operationText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("composite-asc", operationText, StringComparison.OrdinalIgnoreCase);
    }
}
