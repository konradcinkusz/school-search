using System.Text.Json;
using SchoolSearch.Api.Demo;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Api.Tests.Security;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>
/// Contract tests for the score carried by GET /api/v1/catchment. A catchment school must carry the same score as
/// GET /api/v1/schools/{slug}: published with value, interval, cohort, version and provenance, or suppressed or
/// missing with an explicit status and no value. Runs on the synthetic demo gmina (DemoSeed) with a few test-only
/// rows added on its street. Every value is fictional.
/// </summary>
public sealed class CatchmentScoreContractTests
{
    /// <summary>Demo school 1: 117 of 120 pupils sat in 2025, so the score is published. Catchment school for Przykladowa 1-20.</summary>
    private const string PublishedSlug = "demo-sp-1";

    /// <summary>Demo school 3: 8 pupils sat, which is the limited band (5 to 8, ADR-0009).</summary>
    private const long LimitedId = 900003;

    /// <summary>Demo school 6: cohorts only, no exam results.</summary>
    private const long NoExamId = 900006;

    /// <summary>Test-only school: 3 pupils sat, below the floor of 5, so insufficient_cohort.</summary>
    private const long InsufficientId = 900099;
    private const string InsufficientSlug = "catchment-test-small";

    private const int DemoExamYear = 2025;
    private const int InsufficientSat = 3;

    [Fact]
    public async Task Published_catchment_school_carries_value_interval_cohort_version_and_provenance()
    {
        using var factory = new ConfiguredApiFactory();
        await SeedAsync(factory);
        var client = factory.CreateClient();

        var root = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync(
            $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Przykladowa&number=5"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        var schools = root.GetProperty("schools");
        Assert.Equal(new[] { PublishedSlug }, ContractNav.Slugs(schools));

        var school = schools.EnumerateArray().Single();
        ContractAssert.ExactKeys(school, ContractKeys.SchoolListItem);
        Assert.Equal("published", school.GetProperty("scoreStatus").GetString());

        var block = school.GetProperty("score");
        ContractAssert.ExactKeys(block, ContractKeys.ScoreBlock);
        var value = block.GetProperty("score");
        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        var low = block.GetProperty("intervalLow");
        var high = block.GetProperty("intervalHigh");
        Assert.Equal(JsonValueKind.Number, low.ValueKind);
        Assert.Equal(JsonValueKind.Number, high.ValueKind);
        Assert.True(low.GetDouble() <= value.GetDouble() && value.GetDouble() <= high.GetDouble());
        Assert.Equal(JsonValueKind.Null, block.GetProperty("reason").ValueKind);
        Assert.Equal(117, block.GetProperty("cohortSize").GetInt32());
        Assert.Equal(ScoringPolicy.MethodologyVersion, block.GetProperty("methodologyVersion").GetString());
        Assert.Equal(DemoExamYear, block.GetProperty("examYear").GetInt32());
        Assert.Equal("2026-01-01", block.GetProperty("asOf").GetString());
        Assert.Equal(DemoSeed.Source, block.GetProperty("source").GetString());

        // The same school on the profile endpoint carries the identical block: one scoring path, one answer.
        var profile = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync($"/api/v1/schools/{PublishedSlug}"));
        Assert.Equal("published", profile.GetProperty("scoreStatus").GetString());
        Assert.Equal(profile.GetProperty("score").GetRawText(), block.GetRawText());
    }

    [Fact]
    public async Task Limited_catchment_school_with_five_to_eight_pupils_has_an_explicit_status_and_no_value()
    {
        using var factory = new ConfiguredApiFactory();
        await SeedAsync(factory);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Przykladowa&number=45"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        var school = root.GetProperty("schools").EnumerateArray().Single();
        Assert.Equal("demo-sp-3", school.GetProperty("slug").GetString());
        Assert.Equal("limited", school.GetProperty("scoreStatus").GetString());

        var block = school.GetProperty("score");
        ContractAssert.ExactKeys(block, ContractKeys.ScoreBlock);
        Assert.Equal(JsonValueKind.Null, block.GetProperty("score").ValueKind);
        Assert.Equal("limited", block.GetProperty("reason").GetString());
        Assert.Equal(8, block.GetProperty("cohortSize").GetInt32());
        Assert.Equal(JsonValueKind.Null, block.GetProperty("intervalLow").ValueKind);
        Assert.Equal(JsonValueKind.Null, block.GetProperty("intervalHigh").ValueKind);
        Assert.Equal(ScoringPolicy.MethodologyVersion, block.GetProperty("methodologyVersion").GetString());
    }

    [Fact]
    public async Task Suppressed_catchment_school_below_the_cohort_floor_has_insufficient_cohort_and_no_value()
    {
        using var factory = new ConfiguredApiFactory();
        await SeedAsync(factory);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Przykladowa&number=65"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        var school = root.GetProperty("schools").EnumerateArray().Single();
        Assert.Equal(InsufficientSlug, school.GetProperty("slug").GetString());
        Assert.Equal("insufficient_cohort", school.GetProperty("scoreStatus").GetString());

        var block = school.GetProperty("score");
        ContractAssert.ExactKeys(block, ContractKeys.ScoreBlock);
        Assert.Equal(JsonValueKind.Null, block.GetProperty("score").ValueKind);
        Assert.Equal("insufficient_cohort", block.GetProperty("reason").GetString());
        Assert.Equal(InsufficientSat, block.GetProperty("cohortSize").GetInt32());
        Assert.Equal(JsonValueKind.Null, block.GetProperty("intervalLow").ValueKind);
        Assert.Equal(JsonValueKind.Null, block.GetProperty("intervalHigh").ValueKind);
    }

    [Fact]
    public async Task Catchment_school_without_exam_data_has_no_exam_data_status_and_no_score_block()
    {
        using var factory = new ConfiguredApiFactory();
        await SeedAsync(factory);

        var root = await JsonTestHelpers.ReadJsonAsync(await factory.CreateClient().GetAsync(
            $"/api/v1/catchment?gmina={DemoSeed.GminaTeryt}&street=Przykladowa&number=85"));

        Assert.Equal("street_list_match", root.GetProperty("match").GetString());
        var school = root.GetProperty("schools").EnumerateArray().Single();
        Assert.Equal("demo-sp-6", school.GetProperty("slug").GetString());
        Assert.Equal("no_exam_data", school.GetProperty("scoreStatus").GetString());
        Assert.Equal(JsonValueKind.Null, school.GetProperty("score").ValueKind);
    }

    /// <summary>
    /// The demo catalog plus test-only rows. The demo street list covers only schools 1, 2 and 7, so three extra
    /// rules put the other cases on the same demo street by house number (41-60, 61-80, 81-100).
    /// </summary>
    private static async Task SeedAsync(ConfiguredApiFactory factory)
    {
        await factory.SeedAsync(async db =>
        {
            await DemoSeed.ApplyAsync(db, CancellationToken.None);

            db.Schools.Add(new School
            {
                IdPodmiot = InsufficientId,
                Name = "Szkoła testowa z trzema uczniami (dane testowe)",
                Slug = InsufficientSlug,
                GminaTeryt = DemoSeed.GminaTeryt,
                GminaName = DemoSeed.GminaName,
                IsPublic = true,
                IsSpecial = false,
                Provenance = Prov(),
            });
            db.ClassCohorts.Add(new ClassCohort
            {
                SchoolId = InsufficientId,
                SchoolYear = DemoExamYear - 1,
                ClassLevel = "VIII",
                Pupils = InsufficientSat,
                Oddzialy = 1,
                Provenance = Prov(),
            });
            foreach (var subject in new[] { "polish", "mathematics", "foreign_language" })
            {
                db.ExamResults.Add(new ExamResult
                {
                    SchoolId = InsufficientId,
                    ExamYear = DemoExamYear,
                    Subject = subject,
                    MeanPercent = 50.0,
                    PupilsSat = InsufficientSat,
                    Provenance = Prov(),
                });
            }

            db.CatchmentStreets.Add(StreetRule(LimitedId, 41, 60));
            db.CatchmentStreets.Add(StreetRule(InsufficientId, 61, 80));
            db.CatchmentStreets.Add(StreetRule(NoExamId, 81, 100));
        });
    }

    private static CatchmentStreet StreetRule(long schoolId, int numberFrom, int numberTo) => new()
    {
        GminaTeryt = DemoSeed.GminaTeryt,
        SchoolId = schoolId,
        StreetNormalized = "przykladowa",
        NumberFrom = numberFrom,
        NumberTo = numberTo,
        Parity = null,
        Provenance = Prov(),
    };

    private static Provenance Prov() => new()
    {
        Source = DemoSeed.Source,
        SourceUrl = DemoSeed.SourceUrl,
        AsOf = DemoSeed.AsOf,
        Method = DemoSeed.Method,
    };
}
