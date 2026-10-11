using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SchoolSearch.Api.Tests;

public sealed class SchoolsEndpointTests : IDisposable
{
    private readonly SchoolSearchApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Page_size_above_the_cap_is_clamped_to_100()
    {
        // 150 rows exist, so an unclamped request for 500 would return 150 and the assertion discriminates.
        await _factory.SeedSchoolsAsync(150);

        var response = await _factory.CreateClient().GetAsync("/api/v1/schools?page=1&pageSize=500");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(100, JsonTestHelpers.CountPageItems(root));
    }

    [Fact]
    public async Task Page_size_below_the_cap_is_honoured()
    {
        await _factory.SeedSchoolsAsync(150);

        var response = await _factory.CreateClient().GetAsync("/api/v1/schools?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(10, JsonTestHelpers.CountPageItems(root));
    }

    [Fact]
    public async Task Unknown_school_returns_404_with_the_uniform_error_shape()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/schools/no-such-school");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);

        // Exactly one top-level key: "error".
        Assert.Equal(new[] { "error" }, root.EnumerateObject().Select(p => p.Name).ToArray());

        var error = root.GetProperty("error");
        // Exactly the three keys of the uniform shape.
        Assert.Equal(new[] { "code", "message", "traceId" },
            error.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("code").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Negative_page_is_clamped_to_the_first_page()
    {
        await _factory.SeedSchoolsAsync(3);

        var response = await _factory.CreateClient().GetAsync("/api/v1/schools?page=-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(3, JsonTestHelpers.CountPageItems(root));
    }

    [Fact]
    public async Task Each_factory_gets_its_own_database()
    {
        await _factory.SeedSchoolsAsync(3);

        var ownResponse = await _factory.CreateClient().GetAsync("/api/v1/schools?page=1&pageSize=10");
        Assert.Equal(3, JsonTestHelpers.CountPageItems(await JsonTestHelpers.ReadJsonAsync(ownResponse)));

        using var otherFactory = new SchoolSearchApiFactory();
        var otherResponse = await otherFactory.CreateClient().GetAsync("/api/v1/schools?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        Assert.Equal(0, JsonTestHelpers.CountPageItems(await JsonTestHelpers.ReadJsonAsync(otherResponse)));
    }
}
