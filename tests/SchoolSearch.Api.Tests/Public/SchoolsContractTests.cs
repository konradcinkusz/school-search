using System.Net;
using System.Text.Json;
using Xunit;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>Contract tests for GET /api/v1/schools (list, filters, sort, paging, score blocks).</summary>
public sealed class SchoolsContractTests
{
    [Fact]
    public async Task Page_and_item_keys_match_the_contract()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync("/api/v1/schools?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, ContractKeys.SchoolPage);
        Assert.Equal(7, root.GetProperty("total").GetInt32());

        var items = root.GetProperty("items");
        var first = items.EnumerateArray().First();
        ContractAssert.ExactKeys(first, ContractKeys.SchoolListItem);
    }

    [Fact]
    public async Task Published_score_block_is_complete_and_carries_the_engine_arithmetic()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync("/api/v1/schools?pageSize=100"));
        var school = ContractNav.BySlug(root.GetProperty("items"), PublicSeed.SchoolA);

        Assert.Equal("published", school.GetProperty("scoreStatus").GetString());
        var score = school.GetProperty("score");
        ContractAssert.ExactKeys(score, ContractKeys.ScoreBlock);

        // Mean 90 at coverage 0.5: factor 0.625, effective 56.25, minus the national mean 60.
        Assert.Equal(-3.75, score.GetProperty("score").GetDouble(), 9);
        Assert.Equal(JsonValueKind.Null, score.GetProperty("reason").ValueKind);
        Assert.Equal(10, score.GetProperty("cohortSize").GetInt32());
        Assert.Equal("1.0.0", score.GetProperty("methodologyVersion").GetString());
        Assert.Equal(PublicSeed.ExamYear, score.GetProperty("examYear").GetInt32());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", score.GetProperty("asOf").GetString() ?? string.Empty);
        Assert.False(string.IsNullOrWhiteSpace(score.GetProperty("source").GetString()));

        var low = score.GetProperty("intervalLow").GetDouble();
        var high = score.GetProperty("intervalHigh").GetDouble();
        Assert.True(low < -3.75 && -3.75 < high, $"Interval [{low}, {high}] must bracket the score.");
    }

    [Fact]
    public async Task Suppressed_school_has_null_score_and_insufficient_cohort_reason_never_zero()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync("/api/v1/schools?pageSize=100"));
        var school = ContractNav.BySlug(root.GetProperty("items"), PublicSeed.SchoolE);

        Assert.Equal("insufficient_cohort", school.GetProperty("scoreStatus").GetString());
        var score = school.GetProperty("score");
        ContractAssert.ExactKeys(score, ContractKeys.ScoreBlock);
        Assert.Equal(JsonValueKind.Null, score.GetProperty("score").ValueKind);
        Assert.Equal("insufficient_cohort", score.GetProperty("reason").GetString());
        Assert.Equal(5, score.GetProperty("cohortSize").GetInt32());
        Assert.Equal(JsonValueKind.Null, score.GetProperty("intervalLow").ValueKind);
        Assert.Equal(JsonValueKind.Null, score.GetProperty("intervalHigh").ValueKind);
    }

    [Fact]
    public async Task School_without_exam_results_has_no_exam_data_status_and_a_null_score_block()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync("/api/v1/schools?pageSize=100"));
        var school = ContractNav.BySlug(root.GetProperty("items"), PublicSeed.SchoolG);

        Assert.Equal("no_exam_data", school.GetProperty("scoreStatus").GetString());
        Assert.Equal(JsonValueKind.Null, school.GetProperty("score").ValueKind);
    }

    [Fact]
    public async Task Sort_by_score_is_descending_and_puts_unscored_schools_last()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync("/api/v1/schools?sort=score&pageSize=100"));
        var slugs = ContractNav.Slugs(root.GetProperty("items"));
        var count = slugs.Length;
        Assert.Equal(7, count);

        // Published: C and D tie at +10 (order within a tie is not part of the contract), then B +2, A -3.75, F -20.
        Assert.Equal(new[] { PublicSeed.SchoolC, PublicSeed.SchoolD }, slugs.Take(2).OrderBy(s => s, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { PublicSeed.SchoolB, PublicSeed.SchoolA, PublicSeed.SchoolF }, slugs.Skip(2).Take(3).ToArray());

        // Unscored last: suppressed E and the school with no exam data G.
        Assert.Equal(new[] { PublicSeed.SchoolE, PublicSeed.SchoolG }, slugs.Skip(5).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Sort_direction_parameters_are_ignored_and_never_reverse_the_order()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        var descending = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools?sort=score&pageSize=100"));
        var attempted = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync(
            "/api/v1/schools?sort=score&pageSize=100&order=asc&direction=asc&dir=asc&asc=true&ascending=true&reverse=true"));

        Assert.Equal(ContractNav.Slugs(descending.GetProperty("items")), ContractNav.Slugs(attempted.GetProperty("items")));
    }

    [Fact]
    public async Task Name_query_is_a_case_insensitive_substring_of_the_name()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync("/api/v1/schools?q=NR%205"));

        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal(new[] { PublicSeed.SchoolE }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task Gmina_filter_returns_only_schools_of_that_gmina()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/schools?gmina={PublicSeed.GminaWithStreetList}&pageSize=100"));

        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(
            new[] { PublicSeed.SchoolA, PublicSeed.SchoolB, PublicSeed.SchoolC, PublicSeed.SchoolD },
            ContractNav.Slugs(root.GetProperty("items")).OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Is_public_filter_excludes_non_public_schools()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        var publicOnly = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools?isPublic=true"));
        var notPublic = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools?isPublic=false"));

        Assert.Equal(7, publicOnly.GetProperty("total").GetInt32());
        Assert.Equal(0, notPublic.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Page_size_is_clamped_to_100_and_paging_reports_the_total()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        var clamped = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools?pageSize=500"));
        Assert.Equal(100, clamped.GetProperty("pageSize").GetInt32());

        var lastPage = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools?page=3&pageSize=3"));
        Assert.Equal(3, lastPage.GetProperty("page").GetInt32());
        Assert.Equal(7, lastPage.GetProperty("total").GetInt32());
        Assert.Equal(1, ContractNav.Length(lastPage.GetProperty("items")));
    }
}
