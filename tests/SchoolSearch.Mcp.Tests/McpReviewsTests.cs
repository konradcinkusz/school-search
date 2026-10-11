using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// Protocol tests for get_school_reviews (E5). The Reviews service is stubbed. The tool forwards the page arguments
/// and returns the envelope verbatim, marked userGenerated=true. No key in the reply may name a score, and an
/// unreachable or unconfigured Reviews service is one structured reviews_unavailable error.
/// </summary>
public sealed class McpReviewsTests
{
    private const string ToolName = "get_school_reviews";
    private const string ScoreKeyPattern = "score|average|rank|grade|quality|best|worst";

    [Fact]
    public async Task Get_school_reviews_returns_the_envelope_marked_user_generated()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "szkola-podstawowa-1" });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var root = document.RootElement;
        Assert.True(root.GetProperty("userGenerated").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("notice").GetString()));
        Assert.Equal(1, root.GetProperty("total").GetInt32());

        var item = root.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Rodzic/opiekun (niezweryfikowany)", item.GetProperty("authorLabel").GetString());
        Assert.Equal(JsonValueKind.Object, item.GetProperty("reply").ValueKind);
        Assert.Equal("unverified", item.GetProperty("reply").GetProperty("verificationState").GetString());
    }

    [Fact]
    public async Task Get_school_reviews_forwards_the_slug_page_and_page_size()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject
        {
            ["slug"] = "szkola-podstawowa-1",
            ["page"] = 2,
            ["pageSize"] = 5,
        });

        Assert.False(reply.IsError, reply.Text);
        var request = factory.ReviewsStub.Requests.Single();
        Assert.Equal("/api/v1/schools/szkola-podstawowa-1/reviews", request.AbsolutePath);
        Assert.Contains("page=2", request.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=5", request.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Catalog.Requests, u => u.AbsolutePath.Contains("reviews", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_school_reviews_clamps_the_page_size_to_fifty()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "szkola-podstawowa-1", ["pageSize"] = 500 });

        var request = factory.ReviewsStub.Requests.Single();
        Assert.Contains("pageSize=50", request.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_school_reviews_envelope_has_no_score_like_keys()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "szkola-podstawowa-1" });

        Assert.False(reply.IsError, reply.Text);
        using var document = JsonDocument.Parse(reply.Text);
        var root = document.RootElement;
        foreach (var property in root.EnumerateObject())
        {
            Assert.False(Regex.IsMatch(property.Name, ScoreKeyPattern, RegexOptions.IgnoreCase),
                $"the reviews envelope must not carry a score-like key: '{property.Name}'");
        }

        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            foreach (var property in item.EnumerateObject())
            {
                Assert.False(Regex.IsMatch(property.Name, ScoreKeyPattern, RegexOptions.IgnoreCase),
                    $"a review must not carry a score-like key: '{property.Name}'");
            }
        }
    }

    [Fact]
    public async Task Get_school_reviews_description_says_user_generated_and_never_averaged()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var tool = (await mcp.ListToolsAsync()).Single(t => t.GetProperty("name").GetString() == ToolName);
        var description = tool.GetProperty("description").GetString() ?? string.Empty;

        Assert.Contains("user-generated", description, StringComparison.Ordinal);
        Assert.Contains("never average", description, StringComparison.Ordinal);
        Assert.Contains("not a quality measure", description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unconfigured_reviews_answer_a_structured_reviews_unavailable_error_without_a_call()
    {
        using var factory = new McpTestFactory { ReviewsConfigured = false };
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "szkola-podstawowa-1" });

        Assert.True(reply.IsError, "an unconfigured reviews service must surface as isError=true");
        using var document = JsonDocument.Parse(reply.Text);
        Assert.Equal("reviews_unavailable", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(factory.ReviewsStub.Requests);
    }

    [Fact]
    public async Task Reviews_upstream_500_becomes_reviews_unavailable_without_a_stack_trace()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());
        factory.ReviewsStub.UpstreamFailure = true;

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "szkola-podstawowa-1" });

        Assert.True(reply.IsError, "an upstream 500 must surface as isError=true");
        using var document = JsonDocument.Parse(reply.Text);
        Assert.Equal("reviews_unavailable", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("   at ", reply.Text);
        Assert.DoesNotContain("Exception", reply.Text);
    }

    [Fact]
    public async Task Unknown_school_passes_the_reviews_error_code_through()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "nie-ma-takiej-szkoly" });

        Assert.True(reply.IsError, "an unknown school must surface as isError=true");
        using var document = JsonDocument.Parse(reply.Text);
        Assert.Equal("not_found", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("   at ", reply.Text);
    }

    [Fact]
    public async Task Empty_slug_is_rejected_before_any_reviews_call()
    {
        using var factory = new McpTestFactory();
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ToolName, new JsonObject { ["slug"] = "   " });

        Assert.True(reply.IsError, "an empty slug must be a tool error");
        Assert.Contains("validation_failed", reply.Text, StringComparison.Ordinal);
        Assert.Empty(factory.ReviewsStub.Requests);
    }
}
