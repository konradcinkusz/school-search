using System.Net;
using System.Text.Json;
using SchoolSearch.Api.Demo;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Api.Tests.Security;
using Xunit;

namespace SchoolSearch.Api.Tests.Preschools;

public sealed class DemoPreschoolSeedTests : IDisposable
{
    private readonly ConfiguredApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Demo_seed_adds_a_mixed_synthetic_set_once_and_repeat_runs_add_nothing()
    {
        var firstRun = 0;
        var secondRun = 0;
        await _factory.SeedAsync(async db =>
        {
            firstRun = await DemoPreschoolSeed.ApplyAsync(db, CancellationToken.None);
            secondRun = await DemoPreschoolSeed.ApplyAsync(db, CancellationToken.None);
        });

        Assert.True(firstRun > 0);
        Assert.Equal(0, secondRun);

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/preschools?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(5, root.GetProperty("total").GetInt32());

        var items = root.GetProperty("items");
        var publicCount = items.EnumerateArray().Count(i => i.GetProperty("isPublic").GetBoolean());
        Assert.Equal(3, publicCount);
        Assert.Equal(5, ContractNav.Length(items));
        Assert.Equal(1, items.EnumerateArray().Count(i => i.GetProperty("freePlacesCount").ValueKind == JsonValueKind.Number
            && i.GetProperty("freePlacesCount").GetInt32() == 0));

        var withoutFacts = await client.GetAsync("/api/v1/preschools/przedszkole-demonstracyjne-nr-4");
        Assert.Equal(HttpStatusCode.OK, withoutFacts.StatusCode);
        var profile = await JsonTestHelpers.ReadJsonAsync(withoutFacts);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("facts").ValueKind);
    }
}
