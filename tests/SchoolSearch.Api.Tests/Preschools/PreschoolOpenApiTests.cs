using System.Net;
using SchoolSearch.Api.Tests.Security;
using Xunit;

namespace SchoolSearch.Api.Tests.Preschools;

/// <summary>
/// Preschool operations in the published OpenAPI document (ADR-0014): the list takes exactly the documented filters and
/// no sort or direction parameter, and the profile takes only the slug.
/// </summary>
public sealed class PreschoolOpenApiTests
{
    private static readonly string[] ListParameters =
    [
        "q", "gmina", "lat", "lon", "radiusKm", "publicOnly", "maxTuition", "hasFreePlaces", "page", "pageSize",
    ];

    [Fact]
    public async Task Preschool_list_takes_only_the_documented_parameters()
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await JsonTestHelpers.ReadJsonAsync(response);
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/preschools", out var list), "/api/v1/preschools is not in the OpenAPI document.");

        var names = list.GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString() ?? string.Empty)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ListParameters.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            names);
    }

    [Fact]
    public async Task Preschool_profile_is_addressed_by_slug_only()
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        var document = await JsonTestHelpers.ReadJsonAsync(response);
        var paths = document.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/preschools/{slug}", out var profile), "/api/v1/preschools/{slug} is not documented.");

        var names = profile.GetProperty("get").TryGetProperty("parameters", out var parameters)
            ? parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
        Assert.Equal(new[] { "slug" }, names);
    }
}
