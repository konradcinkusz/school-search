using System.Net;
using System.Text.Json;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Api.Tests.Security;
using Xunit;
using static SchoolSearch.Api.Tests.Preschools.PreschoolFixtures;

namespace SchoolSearch.Api.Tests.Preschools;

public sealed class PreschoolListTests : IDisposable
{
    private readonly ConfiguredApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Lists_public_and_non_public_preschools_alphabetically_in_the_page_shape()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync(string.Empty);

        ContractAssert.ExactKeys(root, "items", "total", "page", "pageSize");
        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());

        var items = root.GetProperty("items");
        Assert.Equal(new[] { Alfa, Beta, Delta, Gamma }, ContractNav.Slugs(items));
        Assert.False(ContractNav.BySlug(items, Beta).GetProperty("isPublic").GetBoolean());
    }

    [Fact]
    public async Task List_item_has_the_exact_public_keys_and_nullable_facts_stay_null()
    {
        await _factory.SeedAsync(ApplyAsync);

        var items = (await ListAsync(string.Empty)).GetProperty("items");

        var alfa = ContractNav.BySlug(items, Alfa);
        ContractAssert.ExactKeys(
            alfa,
            "slug", "name", "gminaTeryt", "gminaName", "isPublic", "latitude", "longitude",
            "distanceKm", "monthlyTuition", "freePlacesCount");
        Assert.Equal(300m, alfa.GetProperty("monthlyTuition").GetProperty("minPln").GetDecimal());
        Assert.Equal(450m, alfa.GetProperty("monthlyTuition").GetProperty("maxPln").GetDecimal());
        Assert.Equal(2, alfa.GetProperty("freePlacesCount").GetInt32());

        var gamma = ContractNav.BySlug(items, Gamma);
        Assert.Equal(JsonValueKind.Null, gamma.GetProperty("monthlyTuition").GetProperty("minPln").ValueKind);
        Assert.Equal(JsonValueKind.Null, gamma.GetProperty("freePlacesCount").ValueKind);
    }

    [Fact]
    public async Task Without_a_point_distance_is_null()
    {
        await _factory.SeedAsync(ApplyAsync);

        var items = (await ListAsync(string.Empty)).GetProperty("items");

        Assert.Equal(JsonValueKind.Null, ContractNav.BySlug(items, Alfa).GetProperty("distanceKm").ValueKind);
    }

    [Fact]
    public async Task Pages_are_cut_after_the_name_order()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?page=2&pageSize=2");

        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("page").GetInt32());
        Assert.Equal(2, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(new[] { Delta, Gamma }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_empty_with_the_true_total()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?page=9");

        Assert.Equal(4, root.GetProperty("total").GetInt32());
        Assert.Equal(0, ContractNav.Length(root.GetProperty("items")));
    }

    [Fact]
    public async Task Gmina_filter_keeps_only_that_gmina()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?gmina=" + GminaOne);

        Assert.Equal(3, root.GetProperty("total").GetInt32());
        Assert.Equal(new[] { Alfa, Beta, Delta }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task Malformed_gmina_is_refused_with_validation_failed()
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools?gmina=12");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Public_only_hides_non_public_preschools()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?publicOnly=true");

        Assert.Equal(new[] { Alfa, Delta, Gamma }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task Max_tuition_keeps_preschools_whose_minimum_fits_and_drops_unknown_tuition()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?maxTuition=400");

        // Beta (minimum 800) is over, Delta (unknown) and Gamma (no facts) never match a ceiling.
        Assert.Equal(new[] { Alfa }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Theory]
    [InlineData("?maxTuition=abc")]
    [InlineData("?maxTuition=-5")]
    public async Task Invalid_max_tuition_is_refused(string query)
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools" + query);

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Has_free_places_true_keeps_only_positive_counts()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?hasFreePlaces=true");

        Assert.Equal(new[] { Alfa, Delta }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task Has_free_places_false_keeps_only_a_reported_zero()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?hasFreePlaces=false");

        Assert.Equal(new[] { Beta }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task Name_search_is_a_case_insensitive_literal_substring()
    {
        await _factory.SeedAsync(ApplyAsync);

        Assert.Equal(new[] { Gamma }, ContractNav.Slugs((await ListAsync("?q=GAM")).GetProperty("items")));

        // Two LIKE wildcards are literal characters, so nothing matches them.
        var wildcards = await ListAsync("?q=%25%25");
        Assert.Equal(0, wildcards.GetProperty("total").GetInt32());

        // A one-character term is ignored, as for schools.
        Assert.Equal(4, (await ListAsync("?q=p")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Radius_search_returns_only_preschools_inside_it_with_distance()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?lat=52.0&lon=19.0&radiusKm=1");

        var items = root.GetProperty("items");
        Assert.Equal(new[] { Alfa, Delta }, ContractNav.Slugs(items));
        Assert.Equal(0d, ContractNav.BySlug(items, Alfa).GetProperty("distanceKm").GetDouble());
        var deltaKm = ContractNav.BySlug(items, Delta).GetProperty("distanceKm").GetDouble();
        Assert.InRange(deltaKm, 0.01d, 1d);
    }

    [Fact]
    public async Task Radius_defaults_to_ten_kilometres_when_omitted()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?lat=52.0&lon=19.0");

        Assert.Equal(new[] { Alfa, Beta, Delta }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Theory]
    [InlineData("?lat=95&lon=19")]
    [InlineData("?lat=52")]
    [InlineData("?lat=52&lon=19&radiusKm=0")]
    [InlineData("?lat=52&lon=19&radiusKm=51")]
    [InlineData("?lat=52&lon=19&radiusKm=abc")]
    public async Task Invalid_geo_parameters_are_refused(string query)
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools" + query);

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Sort_parameter_is_ignored_and_the_order_stays_alphabetical()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync("?sort=score");

        Assert.Equal(new[] { Alfa, Beta, Delta, Gamma }, ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task List_carries_no_score_rank_or_grade_keys()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await ListAsync(string.Empty);

        ContractAssert.AllPropertyNames(root).Should().NotContainScoreWords();
    }

    private async Task<JsonElement> ListAsync(string query)
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/preschools" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonTestHelpers.ReadJsonAsync(response);
    }
}
