using System.Net;
using System.Text.Json;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Api.Tests.Security;
using Xunit;
using static SchoolSearch.Api.Tests.Preschools.PreschoolFixtures;

namespace SchoolSearch.Api.Tests.Preschools;

public sealed class PreschoolProfileTests : IDisposable
{
    private readonly ConfiguredApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Profile_has_the_exact_keys_and_the_fixed_notice()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ProfileAsync(Alfa);

        ContractAssert.ExactKeys(
            root,
            "slug", "name", "gminaTeryt", "gminaName", "isPublic", "street", "postalCode", "city",
            "latitude", "longitude", "governingBody", "facts", "notice");
        Assert.Equal(Notice, root.GetProperty("notice").GetString());
        Assert.Equal("Przedszkole Alfa", root.GetProperty("name").GetString());
        Assert.Equal("ul. Testowa 1", root.GetProperty("street").GetString());
    }

    [Fact]
    public async Task Every_fact_carries_its_own_source_date_and_confidence()
    {
        await _factory.SeedAsync(ApplyAsync);

        var facts = (await ProfileAsync(Alfa)).GetProperty("facts");

        ContractAssert.ExactKeys(facts, "monthlyTuition", "monthlyMealPln", "hours", "freePlaces", "source");
        Assert.Equal(300m, facts.GetProperty("monthlyTuition").GetProperty("minPln").GetDecimal());
        Assert.Equal(450m, facts.GetProperty("monthlyTuition").GetProperty("maxPln").GetDecimal());
        Assert.Equal(100m, facts.GetProperty("monthlyMealPln").GetDecimal());
        Assert.Equal("06:30", facts.GetProperty("hours").GetProperty("opens").GetString());
        Assert.Equal("17:00", facts.GetProperty("hours").GetProperty("closes").GetString());
        Assert.Equal(2, facts.GetProperty("freePlaces").GetProperty("count").GetInt32());
        Assert.Equal("2025-09-01", facts.GetProperty("freePlaces").GetProperty("asOf").GetString());

        var source = facts.GetProperty("source");
        ContractAssert.ExactKeys(source, "source", "sourceUrl", "asOf", "method", "confidence");
        Assert.Equal("Synthetic tuition table", source.GetProperty("source").GetString());
        Assert.Equal("2025-08-15", source.GetProperty("asOf").GetString());
        Assert.Equal("reported", source.GetProperty("confidence").GetString());
    }

    [Fact]
    public async Task Facts_are_null_when_no_facts_row_exists()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ProfileAsync(Gamma);

        Assert.Equal(JsonValueKind.Null, root.GetProperty("facts").ValueKind);
        Assert.Equal(Notice, root.GetProperty("notice").GetString());
    }

    [Fact]
    public async Task Unknown_values_inside_a_facts_row_stay_null_and_are_not_estimated()
    {
        await _factory.SeedAsync(ApplyAsync);

        var facts = (await ProfileAsync(Delta)).GetProperty("facts");

        var tuition = facts.GetProperty("monthlyTuition");
        Assert.Equal(JsonValueKind.Null, tuition.GetProperty("minPln").ValueKind);
        Assert.Equal(JsonValueKind.Null, tuition.GetProperty("maxPln").ValueKind);
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("monthlyMealPln").ValueKind);
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("hours").GetProperty("opens").ValueKind);
        Assert.Equal(JsonValueKind.Null, facts.GetProperty("hours").GetProperty("closes").ValueKind);
        Assert.Equal(5, facts.GetProperty("freePlaces").GetProperty("count").GetInt32());
        Assert.Equal("unverified", facts.GetProperty("source").GetProperty("confidence").GetString());
    }

    [Fact]
    public async Task Non_public_preschool_is_served_with_the_governing_body_type_only()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ProfileAsync(Beta);

        Assert.False(root.GetProperty("isPublic").GetBoolean());
        var body = root.GetProperty("governingBody");
        ContractAssert.ExactKeys(body, "typeCode", "typeLabel", "labelVerified", "isNaturalPerson");
        Assert.Equal(141, body.GetProperty("typeCode").GetInt32());
        Assert.True(body.GetProperty("isNaturalPerson").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("typeLabel").ValueKind);
    }

    [Fact]
    public async Task Unknown_slug_returns_the_uniform_not_found()
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools/przedszkole-nieistniejace");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Malformed_slug_is_refused_with_validation_failed()
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools/Zle_Slug");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Profile_carries_no_score_rank_or_grade_keys()
    {
        await _factory.SeedAsync(ApplyAsync);

        AssertNoScoreKeys(await ProfileAsync(Alfa));
        AssertNoScoreKeys(await ProfileAsync(Gamma));
    }

    private async Task<JsonElement> ProfileAsync(string slug)
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools/" + slug);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonTestHelpers.ReadJsonAsync(response);
    }
}
