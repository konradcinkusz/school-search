using System.Net;
using System.Text.Json;
using SchoolSearch.Api.Tests.Public;
using SchoolSearch.Api.Tests.Security;
using Xunit;
using static SchoolSearch.Api.Tests.Map.MapFixtures;

namespace SchoolSearch.Api.Tests.Map;

/// <summary>Contract tests for GET /api/v1/map/markers (bbox, kinds, filters, truncation, ordering, no judgement keys).</summary>
public sealed class MapMarkersTests : IDisposable
{
    private readonly ConfiguredApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Response_and_marker_keys_match_the_contract_and_are_cacheable()
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/map/markers?" + Box);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=60", response.Headers.CacheControl?.ToString());
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "items", "total", "truncated", "limit");
        Assert.Equal(5, root.GetProperty("total").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(300, root.GetProperty("limit").GetInt32());

        var items = root.GetProperty("items");
        var school = ContractNav.BySlug(items, SchoolA);
        ContractAssert.ExactKeys(
            school,
            "kind", "slug", "name", "latitude", "longitude", "isPublic", "gminaName", "monthlyTuition", "freePlacesCount");
        Assert.Equal("school", school.GetProperty("kind").GetString());
        Assert.Equal("Szkola Alfa", school.GetProperty("name").GetString());
        Assert.Equal(52.00, school.GetProperty("latitude").GetDouble(), 6);
        Assert.Equal(19.00, school.GetProperty("longitude").GetDouble(), 6);
        Assert.True(school.GetProperty("isPublic").GetBoolean());
        Assert.Equal("Gmina testowa pierwsza", school.GetProperty("gminaName").GetString());
        Assert.Equal(JsonValueKind.Null, school.GetProperty("monthlyTuition").ValueKind);
        Assert.Equal(JsonValueKind.Null, school.GetProperty("freePlacesCount").ValueKind);

        var preschool = ContractNav.BySlug(items, PreschoolB);
        ContractAssert.ExactKeys(
            preschool,
            "kind", "slug", "name", "latitude", "longitude", "isPublic", "gminaName", "monthlyTuition", "freePlacesCount");
        Assert.Equal("preschool", preschool.GetProperty("kind").GetString());
        ContractAssert.ExactKeys(preschool.GetProperty("monthlyTuition"), "minPln", "maxPln");
        Assert.Equal(200m, preschool.GetProperty("monthlyTuition").GetProperty("minPln").GetDecimal());
        Assert.Equal(300m, preschool.GetProperty("monthlyTuition").GetProperty("maxPln").GetDecimal());
        Assert.Equal(2, preschool.GetProperty("freePlacesCount").GetInt32());

        var noFacts = ContractNav.BySlug(items, PreschoolE);
        Assert.Equal(JsonValueKind.Null, noFacts.GetProperty("monthlyTuition").GetProperty("minPln").ValueKind);
        Assert.Equal(JsonValueKind.Null, noFacts.GetProperty("freePlacesCount").ValueKind);
    }

    [Fact]
    public async Task Only_entities_inside_the_box_with_coordinates_are_returned()
    {
        await _factory.SeedAsync(ApplyAsync);

        var inside = await GetAsync(Box);
        var slugs = ContractNav.Slugs(inside.GetProperty("items"));
        Assert.DoesNotContain(SchoolFar, slugs);
        Assert.DoesNotContain(PreschoolFar, slugs);
        Assert.DoesNotContain(SchoolNoCoordinates, slugs);

        var outside = await GetAsync("south=60&west=10&north=61&east=11");
        Assert.Empty(outside.GetProperty("items").EnumerateArray());
        Assert.Equal(0L, outside.GetProperty("total").GetInt64());
        Assert.False(outside.GetProperty("truncated").GetBoolean());

        var far = await GetAsync("south=53&west=20&north=54&east=21");
        Assert.Equal(new[] { SchoolFar, PreschoolFar }, ContractNav.Slugs(far.GetProperty("items")));
    }

    [Fact]
    public async Task Items_are_ordered_by_slug_across_both_kinds()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await GetAsync(Box);

        Assert.Equal(
            new[] { SchoolA, PreschoolB, SchoolC, PreschoolD, PreschoolE },
            ContractNav.Slugs(root.GetProperty("items")));
    }

    [Fact]
    public async Task Kind_selects_schools_preschools_or_both()
    {
        await _factory.SeedAsync(ApplyAsync);

        var schools = await GetAsync(Box + "&kind=school");
        Assert.Equal(new[] { SchoolA, SchoolC }, ContractNav.Slugs(schools.GetProperty("items")));
        Assert.Equal(2, schools.GetProperty("total").GetInt32());

        var preschools = await GetAsync(Box + "&kind=preschool");
        Assert.Equal(new[] { PreschoolB, PreschoolD, PreschoolE }, ContractNav.Slugs(preschools.GetProperty("items")));
        Assert.Equal(3, preschools.GetProperty("total").GetInt32());

        var all = await GetAsync(Box + "&kind=all");
        Assert.Equal(5, all.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Q_gmina_and_public_only_filter_both_kinds()
    {
        await _factory.SeedAsync(ApplyAsync);

        var byName = await GetAsync(Box + "&q=alfa");
        Assert.Equal(new[] { SchoolA }, ContractNav.Slugs(byName.GetProperty("items")));

        var byGmina = await GetAsync(Box + "&gmina=" + GminaTwo);
        Assert.Equal(new[] { SchoolC, PreschoolD }, ContractNav.Slugs(byGmina.GetProperty("items")));

        var publicOnly = await GetAsync(Box + "&publicOnly=true");
        Assert.Equal(new[] { SchoolA, PreschoolB, PreschoolE }, ContractNav.Slugs(publicOnly.GetProperty("items")));

        var notRestricted = await GetAsync(Box + "&publicOnly=false");
        Assert.Equal(5, notRestricted.GetProperty("total").GetInt32());

        var shortQuery = await GetAsync(Box + "&q=a");
        Assert.Equal(5, shortQuery.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Preschool_filters_narrow_preschools_and_leave_no_schools_with_kind_all()
    {
        await _factory.SeedAsync(ApplyAsync);

        // Tuition minimum at most 500: only Beta (200). Delta is dearer, Epsilon has no known tuition.
        var cheap = await GetAsync(Box + "&maxTuition=500");
        Assert.Equal(new[] { PreschoolB }, ContractNav.Slugs(cheap.GetProperty("items")));
        Assert.Equal(1, cheap.GetProperty("total").GetInt32());

        // Free places: Beta has 2, Delta has 0, Epsilon is unknown.
        var free = await GetAsync(Box + "&hasFreePlaces=true&kind=preschool");
        Assert.Equal(new[] { PreschoolB }, ContractNav.Slugs(free.GetProperty("items")));

        var both = await GetAsync(Box + "&maxTuition=1000&hasFreePlaces=true");
        Assert.Equal(new[] { PreschoolB }, ContractNav.Slugs(both.GetProperty("items")));

        // hasFreePlaces=false is the same as absent.
        var unrestricted = await GetAsync(Box + "&hasFreePlaces=false");
        Assert.Equal(5, unrestricted.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("&maxTuition=500")]
    [InlineData("&hasFreePlaces=true")]
    public async Task Preschool_only_filters_with_kind_school_are_refused(string filter)
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/map/markers?" + Box + "&kind=school" + filter);

        var error = await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
        Assert.Contains(
            "maxTuition and hasFreePlaces apply to preschools only",
            error.GetProperty("error").GetProperty("message").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limit_truncates_and_total_still_counts_every_match()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await GetAsync(Box + "&limit=1");

        Assert.Equal(5, root.GetProperty("total").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
        Assert.Equal(1, root.GetProperty("limit").GetInt32());
        Assert.Equal(new[] { SchoolA }, ContractNav.Slugs(root.GetProperty("items")));

        var two = await GetAsync(Box + "&limit=2");
        Assert.Equal(new[] { SchoolA, PreschoolB }, ContractNav.Slugs(two.GetProperty("items")));
        Assert.True(two.GetProperty("truncated").GetBoolean());

        var exact = await GetAsync(Box + "&limit=5");
        Assert.False(exact.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Limit_is_clamped_to_the_allowed_range()
    {
        await _factory.SeedAsync(ApplyAsync);

        var high = await GetAsync(Box + "&limit=100000");
        Assert.Equal(500, high.GetProperty("limit").GetInt32());

        var low = await GetAsync(Box + "&limit=0");
        Assert.Equal(1, low.GetProperty("limit").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("south=51.9&west=18.9&north=52.1")]
    [InlineData("south=abc&west=18.9&north=52.1&east=19.1")]
    [InlineData("south=52.1&west=18.9&north=51.9&east=19.1")]
    [InlineData("south=51.9&west=19.1&north=52.1&east=18.9")]
    [InlineData("south=51.9&west=18.9&north=52.1&east=18.9")]
    [InlineData("south=-91&west=18.9&north=52.1&east=19.1")]
    [InlineData("south=51.9&west=18.9&north=91&east=19.1")]
    [InlineData("south=51.9&west=-181&north=52.1&east=19.1")]
    [InlineData("south=51.9&west=18.9&north=52.1&east=181")]
    [InlineData("south=NaN&west=18.9&north=52.1&east=19.1")]
    public async Task Missing_or_invalid_box_is_refused(string query)
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/map/markers?" + query);

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Theory]
    [InlineData("&kind=nursery")]
    [InlineData("&gmina=123")]
    [InlineData("&gmina=12345ab")]
    [InlineData("&publicOnly=maybe")]
    [InlineData("&hasFreePlaces=maybe")]
    [InlineData("&maxTuition=abc")]
    [InlineData("&maxTuition=-5")]
    [InlineData("&maxTuition=12.5")]
    [InlineData("&limit=many")]
    public async Task Invalid_optional_parameters_are_refused(string extra)
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/map/markers?" + Box + extra);

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Response_has_no_score_rank_rating_review_or_billing_keys()
    {
        await _factory.SeedAsync(ApplyAsync);

        var root = await GetAsync(Box);

        string[] forbidden = ["score", "rank", "rating", "grade", "quality", "best", "worst", "review", "sponsor", "billing", "plan"];
        var offenders = ContractAssert.AllPropertyNames(root)
            .Where(name => forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        Assert.Empty(offenders);
    }

    [Fact]
    public async Task Endpoint_is_anonymous()
    {
        await _factory.SeedAsync(ApplyAsync);

        var response = await _factory.CreateClient().GetAsync("/api/v1/map/markers?" + Box);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<JsonElement> GetAsync(string query)
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/map/markers?" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonTestHelpers.ReadJsonAsync(response);
    }
}
