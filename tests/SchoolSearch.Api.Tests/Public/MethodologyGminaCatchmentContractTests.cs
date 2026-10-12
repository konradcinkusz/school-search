using System.Net;
using System.Text.Json;
using SchoolSearch.Api.Demo;
using SchoolSearch.Api.Scoring;
using Xunit;
using SchoolSearch.Api.Tests.Security;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>Contract tests for GET /api/v1/methodology.</summary>
public sealed class MethodologyContractTests
{
    [Fact]
    public async Task Methodology_has_the_exact_keys_and_the_engine_parameters()
    {
        using var factory = new ConfiguredApiFactory();

        var response = await factory.CreateClient().GetAsync("/api/v1/methodology");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, ContractKeys.Methodology);

        // The served parameters are read from ScoringPolicy, the single home of the constants.
        Assert.Equal(ScoringPolicy.MethodologyVersion, root.GetProperty("version").GetString());
        Assert.Equal(ScoringPolicy.PublicationThreshold, root.GetProperty("publicationThreshold").GetInt32());
        Assert.Equal(ScoringPolicy.CoverageFloor, root.GetProperty("coverageFloor").GetDouble(), 9);
        Assert.Equal("equal", root.GetProperty("weights").GetString());
        Assert.Equal(ScoringPolicy.IntervalZ, root.GetProperty("intervalZ").GetDouble(), 9);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("docsUrl").GetString()));

        Assert.DoesNotContain("oficjalny ranking", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Contract tests for GET /api/v1/gminas.</summary>
public sealed class GminasContractTests
{
    [Fact]
    public async Task Gminas_list_carries_school_counts_and_coverage_state()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync("/api/v1/gminas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        foreach (var gmina in root.EnumerateArray())
        {
            ContractAssert.ExactKeys(gmina, ContractKeys.Gmina);
        }

        var withStreetList = root.EnumerateArray().Single(g => g.GetProperty("teryt").GetString() == PublicSeed.GminaWithStreetList);
        Assert.Equal("Gmina testowa pierwsza", withStreetList.GetProperty("name").GetString());
        Assert.Equal(4, withStreetList.GetProperty("schoolCount").GetInt32());
        Assert.Equal("street_list", withStreetList.GetProperty("coverageState").GetString());

        var withoutCatchment = root.EnumerateArray().Single(g => g.GetProperty("teryt").GetString() == PublicSeed.GminaWithoutCatchmentData);
        Assert.Equal(3, withoutCatchment.GetProperty("schoolCount").GetInt32());
        Assert.Equal("none", withoutCatchment.GetProperty("coverageState").GetString());
    }
}

/// <summary>
/// Contract tests for GET /api/v1/catchment. The rules: no boundary is ever synthesised from distance
/// (no_data), a street list answers only for the numbers it covers (street_list_match), a listed street whose rules
/// do not cover the number is no_number_match, and a street that is not listed is no_match.
/// </summary>
public sealed class CatchmentContractTests
{
    [Fact]
    public async Task Gmina_without_catchment_data_answers_no_data_and_lists_no_schools_even_though_it_has_schools()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithoutCatchmentData}&street=Lipowa&number=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, ContractKeys.Catchment);
        Assert.Equal(PublicSeed.GminaWithoutCatchmentData, root.GetProperty("gminaTeryt").GetString());
        Assert.Equal("no_data", root.GetProperty("match").GetString());

        var coverage = root.GetProperty("coverage");
        ContractAssert.ExactKeys(coverage, ContractKeys.CatchmentCoverage);
        Assert.Equal("none", coverage.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("resolutionUrl").ValueKind);

        // Gmina G2 holds three schools; none may be offered by a distance or any other guess.
        Assert.Equal(0, ContractNav.Length(root.GetProperty("schools")));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("disclaimer").GetString()));
    }

    [Fact]
    public async Task Street_list_match_returns_the_school_whose_rule_covers_the_number_and_links_the_resolution()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithStreetList}&street=Lipowa&number=5"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        var coverage = root.GetProperty("coverage");
        Assert.Equal("street_list", coverage.GetProperty("state").GetString());
        Assert.Equal(PublicSeed.StreetListResolutionUrl, coverage.GetProperty("resolutionUrl").GetString());
        Assert.Equal("2024-09-01", coverage.GetProperty("resolutionDate").GetString());

        var schools = root.GetProperty("schools");
        Assert.Equal(new[] { PublicSeed.SchoolB }, ContractNav.Slugs(schools));
        foreach (var school in schools.EnumerateArray())
        {
            ContractAssert.ExactKeys(school, ContractKeys.SchoolListItem);
        }
    }

    [Fact]
    public async Task Number_outside_the_street_range_is_no_number_match()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithStreetList}&street=Lipowa&number=99"));

        Assert.Equal("no_number_match", root.GetProperty("match").GetString());
        Assert.Equal(0, ContractNav.Length(root.GetProperty("schools")));
    }

    [Fact]
    public async Task Street_absent_from_the_list_is_no_match()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithStreetList}&street=Nieistniejaca&number=5"));

        Assert.Equal("no_match", root.GetProperty("match").GetString());
        Assert.Equal("street_list", root.GetProperty("coverage").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Parity_rule_is_applied_to_the_house_number()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);
        var client = factory.CreateClient();

        // School C covers even numbers 1 to 50 on "Dluga": 7 is odd, 8 is even.
        var odd = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithStreetList}&street=Dluga&number=7"));
        Assert.Equal("no_number_match", odd.GetProperty("match").GetString());

        var even = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithStreetList}&street=Dluga&number=8"));
        Assert.Equal("street_list_match", even.GetProperty("match").GetString());
        Assert.Equal(new[] { PublicSeed.SchoolC }, ContractNav.Slugs(even.GetProperty("schools")));
    }

    [Fact]
    public async Task Unknown_street_on_the_demo_gmina_is_no_match_even_with_a_house_number()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(db => DemoSeed.ApplyAsync(db, CancellationToken.None));

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Nieistniejaca&number=45"));

        Assert.Equal("no_match", root.GetProperty("match").GetString());
        Assert.Equal(0, ContractNav.Length(root.GetProperty("schools")));
    }

    [Fact]
    public async Task Listed_street_with_numbers_outside_every_rule_is_no_number_match()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(db => DemoSeed.ApplyAsync(db, CancellationToken.None));
        var client = factory.CreateClient();

        // Demo rules for Przykladowa cover 1 to 40 only, so 45 and 85 are listed streets with no covering rule.
        foreach (var number in new[] { "45", "85" })
        {
            var root = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync(
                $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Przykladowa&number={number}"));

            Assert.Equal("no_number_match", root.GetProperty("match").GetString());
            Assert.Equal(0, ContractNav.Length(root.GetProperty("schools")));
            Assert.Equal("street_list", root.GetProperty("coverage").GetProperty("state").GetString());
        }
    }

    [Fact]
    public async Task Listed_street_with_a_covered_number_keeps_street_list_match()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(db => DemoSeed.ApplyAsync(db, CancellationToken.None));

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Przykladowa&number=5"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        Assert.Equal(new[] { "demo-sp-1" }, ContractNav.Slugs(root.GetProperty("schools")));
    }

    [Fact]
    public async Task Street_name_is_matched_after_diacritics_are_folded()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var query = Uri.EscapeDataString("Lipową");
        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={PublicSeed.GminaWithStreetList}&street={query}&number=5"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        Assert.Equal(new[] { PublicSeed.SchoolB }, ContractNav.Slugs(root.GetProperty("schools")));
    }

    [Fact]
    public async Task Malformed_gmina_is_a_400_with_the_uniform_error_shape()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync("/api/v1/catchment?gmina=abc&street=Lipowa&number=5");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }
}
