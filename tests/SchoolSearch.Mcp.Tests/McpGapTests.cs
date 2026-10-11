using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// Gap closure for the MCP service. (1) The Reviews call carries the shared secret and the visitor address, so Reviews
/// meters the visitor. (2) Preschool and reviews guidance is published as resources and prompts; the preschool prompt
/// forbids quality claims.
/// </summary>
public sealed class McpGapTests
{
    private const string ReviewsTool = "get_school_reviews";
    private const string NoScoreResource = "schoolsearch://about/preschools-no-score";
    private const string ReviewsResource = "schoolsearch://about/reviews";
    private const string ComparePrompt = "compare_preschools_practically";
    private const string VisitorAddress = "203.0.113.7";

    [Fact]
    public async Task Reviews_call_carries_the_shared_secret_and_the_visitor_address()
    {
        using var factory = new GapMcpFactory(withSecret: true);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("Fly-Client-IP", VisitorAddress);
        using var mcp = await McpWireClient.ConnectAsync(http);

        var reply = await mcp.CallToolAsync(ReviewsTool, new JsonObject { ["slug"] = "szkola-podstawowa-1" });

        Assert.False(reply.IsError, reply.Text);
        var call = Assert.Single(factory.Reviews.Calls);
        Assert.Equal(GapMcpFactory.Secret, call["X-Bff-Secret"]);
        Assert.Equal(VisitorAddress, call["X-Client-Ip"]);
    }

    [Fact]
    public async Task Reviews_call_sends_no_shared_secret_when_none_is_configured()
    {
        using var factory = new GapMcpFactory(withSecret: false);
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var reply = await mcp.CallToolAsync(ReviewsTool, new JsonObject { ["slug"] = "szkola-podstawowa-1" });

        Assert.False(reply.IsError, reply.Text);
        var call = Assert.Single(factory.Reviews.Calls);
        Assert.DoesNotContain(call.Keys, key => key == "X-Bff-Secret");
        Assert.DoesNotContain(call.Keys, key => key == "X-Client-Ip");
    }

    [Fact]
    public async Task Preschools_no_score_resource_is_readable_and_states_the_notice()
    {
        using var factory = new GapMcpFactory(withSecret: false);
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var text = await ReadResourceTextAsync(mcp, NoScoreResource);

        Assert.Contains("Przedszkola nie mają wyniku jakości.", text, StringComparison.Ordinal);
        Assert.Contains("no quality score", text, StringComparison.Ordinal);
        Assert.Contains("never zero", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reviews_guidance_resource_is_readable_and_keeps_reviews_outside_any_score()
    {
        using var factory = new GapMcpFactory(withSecret: false);
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var text = await ReadResourceTextAsync(mcp, ReviewsResource);

        Assert.Contains("user-generated", text, StringComparison.Ordinal);
        Assert.Contains("not part of any score", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guidance_resources_are_published_in_the_resource_listings()
    {
        using var factory = new GapMcpFactory(withSecret: false);
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var listed = (await mcp.RequestAsync("resources/list"))
            .GetProperty("resources")
            .EnumerateArray()
            .Select(r => r.GetProperty("uri").GetString() ?? string.Empty)
            .ToArray();
        var templated = (await mcp.RequestAsync("resources/templates/list"))
            .GetProperty("resourceTemplates")
            .EnumerateArray()
            .Select(t => t.GetProperty("uriTemplate").GetString() ?? string.Empty)
            .ToArray();

        Assert.Contains(listed.Concat(templated), uri => uri == NoScoreResource);
        Assert.Contains(listed.Concat(templated), uri => uri == ReviewsResource);
    }

    [Fact]
    public async Task Compare_preschools_prompt_is_listed_and_forbids_quality_claims()
    {
        using var factory = new GapMcpFactory(withSecret: false);
        using var mcp = await McpWireClient.ConnectAsync(factory.CreateClient());

        var names = (await mcp.RequestAsync("prompts/list"))
            .GetProperty("prompts")
            .EnumerateArray()
            .Select(p => p.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(names, name => name == ComparePrompt);
        Assert.Contains(names, name => name == "read_school_reviews_responsibly");

        var prompt = await mcp.RequestAsync("prompts/get", new JsonObject { ["name"] = ComparePrompt });
        var text = prompt.GetProperty("messages").EnumerateArray().Single()
            .GetProperty("content").GetProperty("text").GetString() ?? string.Empty;

        Assert.Contains("no quality score", text, StringComparison.Ordinal);
        Assert.Contains("do not say that one is better", text, StringComparison.Ordinal);
        Assert.Contains("never zero", text, StringComparison.Ordinal);
    }

    private static async Task<string> ReadResourceTextAsync(McpWireClient mcp, string uri)
    {
        var result = await mcp.RequestAsync("resources/read", new JsonObject { ["uri"] = uri });
        return result.GetProperty("contents").EnumerateArray().Single().GetProperty("text").GetString() ?? string.Empty;
    }
}

/// <summary>
/// Hosts the real MCP app. The catalog is stubbed as in McpTestFactory; the Reviews client is stubbed by a handler that
/// records the headers of every call, so the test can see what the service sent. The secret is set only when asked.
/// </summary>
internal sealed class GapMcpFactory(bool withSecret) : WebApplicationFactory<Program>
{
    public const string Secret = "mcp-gap-shared-secret-not-real";
    public const string ReviewsStubBaseUrl = "https://reviews.gap.invalid/";

    public RecordingReviewsHandler Reviews { get; } = new();

    public CatalogStubHandler Catalog { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Reviews:BaseUrl", ReviewsStubBaseUrl);
        builder.UseSetting("SchoolSearch:TrustProxyClientIpHeader", "true");
        if (withSecret)
        {
            builder.UseSetting("BFF_SHARED_SECRET", Secret);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("catalog").ConfigurePrimaryHttpMessageHandler(() => Catalog);
            services.AddHttpClient("reviews").ConfigurePrimaryHttpMessageHandler(() => Reviews);
        });
    }
}

/// <summary>Answers every Reviews call with an empty published envelope and records the security-relevant headers.</summary>
internal sealed class RecordingReviewsHandler : HttpMessageHandler
{
    private static readonly string[] RecordedHeaders = ["X-Bff-Secret", "X-Client-Ip"];

    private const string EmptyEnvelope = """
        {"userGenerated":true,"notice":"Opinie pochodzą od zalogowanych użytkowników.","items":[],"total":0,"page":1,"pageSize":20}
        """;

    public List<Dictionary<string, string>> Calls { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in RecordedHeaders)
        {
            if (request.Headers.TryGetValues(name, out var values))
            {
                headers[name] = string.Join(",", values);
            }
        }

        Calls.Add(headers);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(EmptyEnvelope, Encoding.UTF8, "application/json"),
        });
    }
}
