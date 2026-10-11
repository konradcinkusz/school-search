using System.Net;
using SchoolSearch.Api.Tests.Public;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>GET /api/v1/schools/by-id/{idPodmiot} returns the same profile as the slug endpoint.</summary>
public sealed class SchoolByIdTests
{
    [Fact]
    public async Task By_id_returns_the_same_profile_as_the_slug_endpoint()
    {
        using var factory = new AiApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var http = factory.CreateClient();

        var byId = await http.GetAsync("/api/v1/schools/by-id/1");
        var bySlug = await http.GetAsync($"/api/v1/schools/{PublicSeed.SchoolA}");

        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySlug.StatusCode);
        var byIdJson = await byId.Content.ReadAsStringAsync();
        Assert.Equal(await bySlug.Content.ReadAsStringAsync(), byIdJson);

        var root = await JsonTestHelpers.ReadJsonAsync(byId);
        Assert.Equal(PublicSeed.SchoolA, root.GetProperty("slug").GetString());
        ContractAssert.ExactKeys(root, ContractKeys.SchoolProfile);
    }

    [Fact]
    public async Task Unknown_id_answers_404_with_the_uniform_error_shape()
    {
        using var factory = new AiApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync("/api/v1/schools/by-id/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal("not_found", root.GetProperty("error").GetProperty("code").GetString());
    }
}
