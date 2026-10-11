using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// Protocol tests for the preschool tools (ADR-0014). A preschool never has a quality score, so no key in any reply
/// may name one. The catalog is stubbed; the tools forward arguments and pass the Api JSON through.
/// </summary>
public sealed class McpPreschoolTests
{
    private const string ScoreKeyPattern = "score|rank|rating|grade|quality|best|worst";

    private static readonly string[] PreschoolToolNames = ["find_preschools", "get_preschool"];

    [Fact]
    public async Task Find_preschools_forwards_the_filters_and_returns_the_api_page()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("find_preschools", new JsonObject
        {
            ["gmina"] = "1465011",
            ["publicOnly"] = true,
            ["maxTuition"] = 450.5,
            ["hasFreePlaces"] = true,
            ["pageSize"] = 20,
        });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("items").GetArrayLength());
        AssertNoScoreKeys(root);

        var request = factory.Catalog.Requests.Single(u => u.AbsolutePath == "/api/v1/preschools");
        Assert.True(request.Query.Contains("gmina=1465011", StringComparison.Ordinal), request.Query);
        Assert.True(request.Query.Contains("publicOnly=true", StringComparison.Ordinal), request.Query);
        Assert.True(request.Query.Contains("maxTuition=450.5", StringComparison.Ordinal), request.Query);
        Assert.True(request.Query.Contains("hasFreePlaces=true", StringComparison.Ordinal), request.Query);
        Assert.True(request.Query.Contains("pageSize=20", StringComparison.Ordinal), request.Query);
    }

    [Fact]
    public async Task Find_preschools_lists_non_public_preschools_and_unknown_facts_as_null()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("find_preschools", new JsonObject { ["q"] = "przedszkole" });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var nonPublic = items.Single(i => !i.GetProperty("isPublic").GetBoolean());
        Assert.Equal(JsonValueKind.Null, nonPublic.GetProperty("monthlyTuition").GetProperty("minPln").ValueKind);
        Assert.Equal(JsonValueKind.Null, nonPublic.GetProperty("freePlacesCount").ValueKind);
    }

    [Fact]
    public async Task Get_preschool_returns_facts_with_source_asof_and_confidence()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("get_preschool", new JsonObject { ["slug"] = "przedszkole-demonstracyjne-1" });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var root = document.RootElement;
        Assert.Equal("przedszkole-demonstracyjne-1", root.GetProperty("slug").GetString());
        Assert.Equal(350m, root.GetProperty("facts").GetProperty("monthlyTuition").GetProperty("minPln").GetDecimal());
        Assert.Equal("16:30", root.GetProperty("facts").GetProperty("hours").GetProperty("closes").GetString());

        var source = root.GetProperty("facts").GetProperty("source");
        Assert.Equal("reported", source.GetProperty("confidence").GetString());
        Assert.Equal("2026-09-01", source.GetProperty("asOf").GetString());
        Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("sourceUrl").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(source.GetProperty("method").GetString()));

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()));
        AssertNoScoreKeys(root);
    }

    [Fact]
    public async Task Get_preschool_without_a_facts_row_reports_null_and_not_zero()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("get_preschool", new JsonObject { ["slug"] = "przedszkole-demonstracyjne-2" });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("facts").ValueKind);
        Assert.False(root.GetProperty("isPublic").GetBoolean());
        AssertNoScoreKeys(root);
    }

    [Fact]
    public async Task Unknown_preschool_slug_is_a_tool_error_with_the_api_code()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("get_preschool", new JsonObject { ["slug"] = "nie-ma-takiego" });

        Assert.True(reply.IsError, "an unknown slug must surface as isError=true");
        using var document = JsonDocument.Parse(reply.Text);
        Assert.Equal("not_found", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("   at ", reply.Text);
    }

    [Fact]
    public async Task Negative_tuition_is_rejected_before_any_catalog_call()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("find_preschools", new JsonObject { ["maxTuition"] = -1 });

        Assert.True(reply.IsError, "a negative maxTuition must be a tool error");
        Assert.Contains("validation_failed", reply.Text);
        Assert.Empty(factory.Catalog.Requests);
    }

    [Fact]
    public async Task Preschool_upstream_500_becomes_a_tool_error_without_a_stack_trace()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());
        factory.Catalog.UpstreamFailure = true;

        var reply = await mcp.CallToolAsync("get_preschool", new JsonObject { ["slug"] = "przedszkole-demonstracyjne-1" });

        Assert.True(reply.IsError, "an upstream 500 must surface as isError=true");
        Assert.False(string.IsNullOrWhiteSpace(reply.Text), "the tool error needs a human-readable message");
        Assert.DoesNotContain("   at ", reply.Text);
        Assert.DoesNotContain("Exception", reply.Text);
    }

    [Fact]
    public async Task Find_preschools_schema_takes_no_sort_or_score_argument()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var tool = (await mcp.ListToolsAsync()).Single(t => t.GetProperty("name").GetString() == "find_preschools");
        var properties = tool.GetProperty("inputSchema").GetProperty("properties");

        foreach (var expected in new[] { "q", "gmina", "publicOnly", "maxTuition", "hasFreePlaces", "lat", "lon", "radiusKm", "page", "pageSize" })
        {
            Assert.True(properties.TryGetProperty(expected, out _), $"find_preschools must accept '{expected}'");
        }

        foreach (var forbidden in new[] { "sort", "order", "direction", "score", "rank", "quality" })
        {
            Assert.False(properties.TryGetProperty(forbidden, out _), $"find_preschools must not accept '{forbidden}'");
        }
    }

    [Fact]
    public async Task Preschool_tools_state_in_their_descriptions_that_there_is_no_quality_score()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var tools = (await mcp.ListToolsAsync())
            .Where(t => PreschoolToolNames.Contains(t.GetProperty("name").GetString() ?? string.Empty))
            .ToArray();

        Assert.Equal(PreschoolToolNames.Length, tools.Length);
        foreach (var tool in tools)
        {
            var description = tool.GetProperty("description").GetString() ?? string.Empty;
            Assert.True(description.Contains("NO quality score", StringComparison.Ordinal), description);
        }
    }

    private static void AssertNoScoreKeys(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Assert.False(
                        Regex.IsMatch(property.Name, ScoreKeyPattern, RegexOptions.IgnoreCase),
                        $"a preschool reply must not carry a score-like key: '{property.Name}'");
                    AssertNoScoreKeys(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AssertNoScoreKeys(item);
                }

                break;
        }
    }
}
