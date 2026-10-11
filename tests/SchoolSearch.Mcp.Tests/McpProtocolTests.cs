using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// Protocol-level tests: raw JSON-RPC over the in-process MCP server, with the catalog stubbed.
/// They pin the tool names, the score-block completeness rule, and the error and no-data behaviour.
/// </summary>
public sealed class McpProtocolTests
{
    private static readonly string[] ExpectedTools =
    [
        "find_schools",
        "get_school",
        "get_catchment_school",
        "compare_schools",
        "get_ranking",
        "get_methodology",
    ];

    [Fact]
    public async Task Initialize_negotiates_a_protocol_version()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var version = mcp.InitializeResult.GetProperty("protocolVersion").GetString();
        Assert.NotNull(version);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", version);
        Assert.True(mcp.InitializeResult.TryGetProperty("capabilities", out _), "initialize must declare server capabilities");
    }

    [Fact]
    public async Task Tools_list_exposes_exactly_the_six_read_only_tools()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var tools = await mcp.ListToolsAsync();
        var names = tools.Select(t => t.GetProperty("name").GetString() ?? string.Empty).ToArray();

        Assert.Equal(ExpectedTools.Order().ToArray(), names.Order().ToArray());
        Assert.DoesNotContain(names, n => Regex.IsMatch(n, "delete|update|write|create", RegexOptions.IgnoreCase));
        foreach (var tool in tools)
        {
            var description = tool.GetProperty("description").GetString();
            Assert.False(string.IsNullOrWhiteSpace(description), "every tool needs a description for the model");
        }
    }

    [Fact]
    public async Task Find_schools_returns_complete_score_blocks_and_a_suppressed_score_as_null_not_zero()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("find_schools", new JsonObject { ["query"] = "szkola" });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "szkola-podstawowa-1", "szkola-podstawowa-2" },
            items.Select(i => i.GetProperty("slug").GetString() ?? string.Empty).ToArray());

        foreach (var item in items)
        {
            AssertCompleteScoreBlock(item.GetProperty("score"));
        }

        var published = items
            .Single(i => i.GetProperty("scoreStatus").GetString() == "published")
            .GetProperty("score");
        Assert.Equal(JsonValueKind.Number, published.GetProperty("score").ValueKind);
        Assert.Equal(54, published.GetProperty("cohortSize").GetInt32());
        Assert.Equal(57.9, published.GetProperty("intervalLow").GetDouble(), 1);
        Assert.Equal(64.9, published.GetProperty("intervalHigh").GetDouble(), 1);
        Assert.Equal("0.1-draft", published.GetProperty("methodologyVersion").GetString());
        Assert.Equal(2025, published.GetProperty("examYear").GetInt32());
        Assert.Equal("2026-06-30", published.GetProperty("asOf").GetString());

        var suppressed = items
            .Single(i => i.GetProperty("scoreStatus").GetString() == "insufficient_cohort")
            .GetProperty("score");
        Assert.Equal(JsonValueKind.Null, suppressed.GetProperty("score").ValueKind);
        Assert.Equal("insufficient_cohort", suppressed.GetProperty("reason").GetString());
        Assert.Equal(7, suppressed.GetProperty("cohortSize").GetInt32());
        Assert.Equal(JsonValueKind.Null, suppressed.GetProperty("intervalLow").ValueKind);
        Assert.Equal(JsonValueKind.Null, suppressed.GetProperty("intervalHigh").ValueKind);
        Assert.Equal("0.1-draft", suppressed.GetProperty("methodologyVersion").GetString());
    }

    [Fact]
    public async Task Get_ranking_schema_takes_no_sort_direction_or_order_argument()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var tool = (await mcp.ListToolsAsync()).Single(t => t.GetProperty("name").GetString() == "get_ranking");
        var properties = tool.GetProperty("inputSchema").GetProperty("properties");

        Assert.True(properties.TryGetProperty("scope", out _), "get_ranking must take a scope argument");
        foreach (var forbidden in new[] { "sort", "direction", "order" })
        {
            Assert.False(properties.TryGetProperty(forbidden, out _), $"get_ranking must not accept '{forbidden}'");
        }
    }

    [Fact]
    public async Task Resource_templates_and_prompts_are_published()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var templates = (await mcp.RequestAsync("resources/templates/list"))
            .GetProperty("resourceTemplates")
            .EnumerateArray()
            .Select(t => t.GetProperty("uriTemplate").GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(templates, t => t.StartsWith("school://", StringComparison.Ordinal));
        Assert.Contains(templates, t => t.StartsWith("methodology://", StringComparison.Ordinal));

        var prompts = (await mcp.RequestAsync("prompts/list"))
            .GetProperty("prompts")
            .EnumerateArray()
            .Select(p => p.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.NotEmpty(prompts);
        Assert.Contains("choose_school_for_my_child", prompts);
    }

    [Fact]
    public async Task Get_catchment_school_reports_no_data_explicitly_and_names_no_school()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync("get_catchment_school", new JsonObject
        {
            ["gmina"] = "1465011",
            ["street"] = "Lipowa",
            ["number"] = "12",
        });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var root = document.RootElement;
        Assert.Equal("no_data", root.GetProperty("match").GetString());
        Assert.Equal("none", root.GetProperty("coverage").GetProperty("state").GetString());
        Assert.Equal(0, root.GetProperty("schools").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("disclaimer").GetString()));
        Assert.Contains(factory.Catalog.Requests, u =>
            u.AbsolutePath == "/api/v1/catchment" && u.Query.Contains("gmina=1465011", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Upstream_500_becomes_a_tool_error_without_a_stack_trace()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());
        factory.Catalog.UpstreamFailure = true;

        var reply = await mcp.CallToolAsync("find_schools", new JsonObject { ["query"] = "szkola" });

        Assert.True(reply.IsError, "an upstream 500 must surface as isError=true");
        Assert.False(string.IsNullOrWhiteSpace(reply.Text), "the tool error needs a human-readable message");
        Assert.DoesNotContain("   at ", reply.Text);
        Assert.DoesNotContain("Exception", reply.Text);
        Assert.DoesNotContain("StackTrace", reply.Text);
    }

    [Fact]
    public async Task Health_endpoint_answers_200()
    {
        using var factory = new McpTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertCompleteScoreBlock(JsonElement block)
    {
        Assert.Equal(JsonValueKind.Object, block.ValueKind);
        foreach (var field in new[]
        {
            "score", "reason", "cohortSize", "intervalLow", "intervalHigh",
            "methodologyVersion", "examYear", "asOf", "source",
        })
        {
            Assert.True(block.TryGetProperty(field, out _), $"score block lacks '{field}': {block.GetRawText()}");
        }
    }
}
