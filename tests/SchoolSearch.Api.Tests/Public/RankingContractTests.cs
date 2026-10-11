using System.Net;
using System.Text.Json;
using Xunit;
using SchoolSearch.Api.Tests.Security;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>Contract tests for GET /api/v1/ranking: descending order, competition ranks, suppression, scopes, paging.</summary>
public sealed class RankingContractTests
{
    [Fact]
    public async Task Country_ranking_is_descending_with_competition_ranks_and_suppressed_schools_outside_items()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/ranking?scope=country&year={PublicSeed.ExamYear}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, ContractKeys.RankingPage);
        Assert.Equal("country", root.GetProperty("scope").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("gminaTeryt").ValueKind);
        Assert.Equal(PublicSeed.ExamYear, root.GetProperty("examYear").GetInt32());
        Assert.Equal("1.0.0", root.GetProperty("methodologyVersion").GetString());

        // Published: C +10 and D +10 (tie, rank 1), B +2 (rank 3, the tie skips 2), A -3.75 (rank 4), F -20 (rank 5).
        var items = root.GetProperty("items");
        Assert.Equal(new[] { PublicSeed.SchoolC, PublicSeed.SchoolD, PublicSeed.SchoolB, PublicSeed.SchoolA, PublicSeed.SchoolF },
            ContractNav.Slugs(items));
        Assert.Equal(new[] { 1, 1, 3, 4, 5 }, items.EnumerateArray().Select(i => i.GetProperty("rank").GetInt32()).ToArray());

        foreach (var item in items.EnumerateArray())
        {
            ContractAssert.ExactKeys(item, ContractKeys.RankedItem);
            ContractAssert.ExactKeys(item.GetProperty("score"), ContractKeys.ScoreBlock);
        }

        // Suppressed E is counted, never listed, never placed at the bottom.
        var suppressed = root.GetProperty("unrankedCount").GetInt32();
        Assert.True(suppressed >= 1, "The insufficient-cohort school must be counted in unrankedCount.");
        Assert.DoesNotContain(PublicSeed.SchoolE, ContractNav.Slugs(items));

        // total is the number of ranked schools (the pagination total of items).
        Assert.Equal(5, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());

        // Evidence that the methodology is not presented as an official ranking.
        Assert.DoesNotContain("oficjalny ranking", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Counter_metric_end_to_end_higher_average_with_lower_coverage_ranks_below()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync(
            $"/api/v1/ranking?scope=gmina&gmina={PublicSeed.GminaWithStreetList}&year={PublicSeed.ExamYear}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal("gmina", root.GetProperty("scope").GetString());
        Assert.Equal(PublicSeed.GminaWithStreetList, root.GetProperty("gminaTeryt").GetString());

        // A has the higher mean (90 against 62) but coverage 0.5, so its score is discounted to -3.75 and it ranks below B.
        var items = root.GetProperty("items");
        Assert.Equal(new[] { PublicSeed.SchoolC, PublicSeed.SchoolD, PublicSeed.SchoolB, PublicSeed.SchoolA },
            ContractNav.Slugs(items));
        Assert.Equal(new[] { 1, 1, 3, 4 }, items.EnumerateArray().Select(i => i.GetProperty("rank").GetInt32()).ToArray());
        Assert.Equal(0, root.GetProperty("unrankedCount").GetInt32());

        var b = ContractNav.BySlug(items, PublicSeed.SchoolB).GetProperty("score").GetProperty("score").GetDouble();
        var a = ContractNav.BySlug(items, PublicSeed.SchoolA).GetProperty("score").GetProperty("score").GetDouble();
        Assert.True(a < b, $"Counter-metric violated: A ({a}) must score below B ({b}).");
    }

    [Fact]
    public async Task Pagination_keeps_the_global_rank_across_pages()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/ranking?scope=country&year={PublicSeed.ExamYear}&page=2&pageSize=2");

        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(5, root.GetProperty("total").GetInt32());
        var items = root.GetProperty("items");
        Assert.Equal(new[] { PublicSeed.SchoolB, PublicSeed.SchoolA }, ContractNav.Slugs(items));
        Assert.Equal(new[] { 3, 4 }, items.EnumerateArray().Select(i => i.GetProperty("rank").GetInt32()).ToArray());
    }

    [Fact]
    public async Task Ranking_takes_no_sort_or_direction_parameter_and_ignores_them()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        var plain = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync($"/api/v1/ranking?scope=country&year={PublicSeed.ExamYear}"));
        var attempted = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync(
            $"/api/v1/ranking?scope=country&year={PublicSeed.ExamYear}&sort=asc&order=asc&direction=asc&dir=asc&desc=true&ascending=true"));

        Assert.Equal(ContractNav.Slugs(plain.GetProperty("items")), ContractNav.Slugs(attempted.GetProperty("items")));
    }

    [Theory]
    [InlineData("scope=gmina")]
    [InlineData("scope=gmina&gmina=12345")]
    [InlineData("scope=sideways")]
    public async Task Invalid_scope_or_missing_gmina_is_a_400(string query)
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/ranking?{query}");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }
}
