using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using SchoolSearch.Api.Tests.Security;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>Contract tests for GET /api/v1/schools/{slug} and GET /api/v1/compare.</summary>
public sealed class SchoolProfileContractTests
{
    [Fact]
    public async Task Profile_has_the_exact_contract_keys_and_nested_shapes()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/schools/{PublicSeed.SchoolA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, ContractKeys.SchoolProfile);
        Assert.Equal(PublicSeed.SchoolA, root.GetProperty("slug").GetString());
        Assert.Equal("1.0.0", root.GetProperty("methodologyVersion").GetString());

        var governingBody = root.GetProperty("governingBody");
        ContractAssert.ExactKeys(governingBody, "typeCode", "typeLabel", "labelVerified", "isNaturalPerson");
        Assert.Equal(130, governingBody.GetProperty("typeCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, governingBody.GetProperty("typeLabel").ValueKind);
        Assert.False(governingBody.GetProperty("labelVerified").GetBoolean());
        Assert.False(governingBody.GetProperty("isNaturalPerson").GetBoolean());

        var faith = root.GetProperty("faith");
        ContractAssert.ExactKeys(faith, "derived", "declared", "note");
        Assert.False(faith.GetProperty("derived").GetBoolean());

        var cohorts = root.GetProperty("cohorts");
        Assert.Equal(1, ContractNav.Length(cohorts));
        var cohort = cohorts.EnumerateArray().First();
        ContractAssert.ExactKeys(cohort, "schoolYear", "classLevel", "pupils", "oddzialy");
        Assert.Equal(PublicSeed.CohortSchoolYear, cohort.GetProperty("schoolYear").GetInt32());
        Assert.Equal("VIII", cohort.GetProperty("classLevel").GetString());
        Assert.Equal(20, cohort.GetProperty("pupils").GetInt32());
        Assert.Equal(2, cohort.GetProperty("oddzialy").GetInt32());
    }

    [Fact]
    public async Task Trend_point_carries_the_score_coverage_and_subject_means()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync($"/api/v1/schools/{PublicSeed.SchoolA}"));
        var point = root.GetProperty("trend").EnumerateArray().Single(t => t.GetProperty("examYear").GetInt32() == PublicSeed.ExamYear);

        ContractAssert.ExactKeys(point, "examYear", "scoreStatus", "score", "coveragePercent", "subjects");
        Assert.Equal("published", point.GetProperty("scoreStatus").GetString());
        Assert.Equal(-3.75, point.GetProperty("score").GetProperty("score").GetDouble(), 9);
        Assert.Equal(50.0, point.GetProperty("coveragePercent").GetDouble(), 9);

        var subjects = point.GetProperty("subjects");
        Assert.Equal(1, ContractNav.Length(subjects));
        var subject = subjects.EnumerateArray().First();
        ContractAssert.ExactKeys(subject, "subject", "meanPercent", "pupilsSat");
        Assert.Equal("polish", subject.GetProperty("subject").GetString());
        Assert.Equal(90.0, subject.GetProperty("meanPercent").GetDouble(), 9);
        Assert.Equal(10, subject.GetProperty("pupilsSat").GetInt32());
    }

    [Fact]
    public async Task Profile_provenance_rows_carry_every_field_with_an_iso_date()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync($"/api/v1/schools/{PublicSeed.SchoolA}"));
        var provenance = root.GetProperty("provenance");

        Assert.True(ContractNav.Length(provenance) > 0, "A published profile must carry provenance.");
        foreach (var row in provenance.EnumerateArray())
        {
            ContractAssert.ExactKeys(row, "field", "source", "sourceUrl", "asOf", "method");
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", row.GetProperty("asOf").GetString() ?? string.Empty);
        }
    }

    [Fact]
    public async Task Profile_of_a_natural_person_governing_body_exposes_the_type_only_never_an_operator_name()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        // School F is governed by type 141 (a natural-person type). The flag is type-level; the name is never modelled.
        var response = await factory.CreateClient().GetAsync($"/api/v1/schools/{PublicSeed.SchoolF}");
        var root = await JsonTestHelpers.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(root.GetProperty("governingBody").GetProperty("isNaturalPerson").GetBoolean());

        var forbidden = new Regex(
            "(?i)operator|organprowadzacy|governingbodyname|teacher|nationality|citizenship|foreign|student|pupilname|personname|surname|firstname|imie|nazwisko",
            RegexOptions.CultureInvariant);
        var offending = ContractAssert.AllPropertyNames(root).Where(name => forbidden.IsMatch(name)).ToArray();
        Assert.Empty(offending);
    }

    [Fact]
    public async Task Unknown_slug_returns_404_with_the_uniform_error_shape()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync("/api/v1/schools/no-such-school");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Compare_returns_two_profiles_in_the_requested_order()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/compare?slugs={PublicSeed.SchoolB},{PublicSeed.SchoolA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        ContractAssert.ExactKeys(root, "items");
        var items = root.GetProperty("items");
        Assert.Equal(2, ContractNav.Length(items));
        Assert.Equal(new[] { PublicSeed.SchoolB, PublicSeed.SchoolA }, ContractNav.Slugs(items));
        foreach (var item in items.EnumerateArray())
        {
            ContractAssert.ExactKeys(item, ContractKeys.SchoolProfile);
        }
    }

    [Fact]
    public async Task Compare_accepts_four_schools()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var slugs = string.Join(',', PublicSeed.SchoolA, PublicSeed.SchoolB, PublicSeed.SchoolC, PublicSeed.SchoolD);
        var response = await factory.CreateClient().GetAsync($"/api/v1/compare?slugs={slugs}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await JsonTestHelpers.ReadJsonAsync(response);
        Assert.Equal(4, ContractNav.Length(root.GetProperty("items")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("szkola-a-wysoka-srednia")]
    [InlineData("szkola-a-wysoka-srednia,szkola-b-pelne-pokrycie,szkola-c-remis,szkola-d-remis,szkola-f-niskie")]
    public async Task Compare_outside_two_to_four_schools_is_a_400(string slugs)
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/compare?slugs={slugs}");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task Compare_with_an_unknown_slug_is_a_uniform_404()
    {
        using var factory = new ConfiguredApiFactory();
        await factory.SeedAsync(PublicSeed.ApplyAsync);

        var response = await factory.CreateClient().GetAsync($"/api/v1/compare?slugs={PublicSeed.SchoolA},no-such-school");

        await ContractAssert.ErrorResponseAsync(response, HttpStatusCode.NotFound, "not_found");
    }
}
