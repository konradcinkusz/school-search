using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Tests.Public;

/// <summary>
/// Synthetic catalog for the public contract tests. Every number is an arithmetic fixture chosen so the expected
/// scores are exact; none is published data and no school or person is real.
/// </summary>
/// <remarks>
/// Scoring arithmetic (baseline for exam year 2025, subject "polish": national mean 60, pupil SD 20 points;
/// the engine applies coverage = sat / headcount and a factor of coverage / 0.8 below 0.8):
///   A szkola-a-wysoka-srednia: mean 90, sat 10, headcount 20 -> coverage 0.5, factor 0.625, score 56.25 - 60 = -3.75
///   B szkola-b-pelne-pokrycie: mean 62, sat 10, headcount 10 -> coverage 1.0, factor 1,     score +2
///   C szkola-c-remis:          mean 70, sat 12, headcount 12 -> score +10
///   D szkola-d-remis:          mean 70, sat 12, headcount 12 -> score +10 (ties with C)
///   E szkola-e-maly-kohorta:   mean 95, sat 5,  headcount 5  -> suppressed (cohort 5 &lt; 9)
///   F szkola-f-niskie:         mean 40, sat 10, headcount 10 -> score -20
///   G szkola-g-bez-wynikow:    no exam result -> no_exam_data
/// A has the highest mean and the lowest coverage, so it ranks below B (the counter-metric, end to end).
/// </remarks>
internal static class PublicSeed
{
    public const int ExamYear = 2025;

    /// <summary>The cohort school year behind an exam year (ExamResult.ExamYear = Y is ClassCohort.SchoolYear = Y-1).</summary>
    public const int CohortSchoolYear = 2024;

    /// <summary>Gmina with a published street list (catchment state street_list).</summary>
    public const string GminaWithStreetList = "9999901";

    /// <summary>Gmina with no catchment coverage row (state none).</summary>
    public const string GminaWithoutCatchmentData = "9999902";

    public const string StreetListResolutionUrl = "https://example.com/synthetic-resolution";

    public const string SchoolA = "szkola-a-wysoka-srednia";
    public const string SchoolB = "szkola-b-pelne-pokrycie";
    public const string SchoolC = "szkola-c-remis";
    public const string SchoolD = "szkola-d-remis";
    public const string SchoolE = "szkola-e-maly-kohorta";
    public const string SchoolF = "szkola-f-niskie";
    public const string SchoolG = "szkola-g-bez-wynikow";

    /// <summary>Adds the whole synthetic catalog. Synchronous work only; the factory saves the context.</summary>
    public static Task ApplyAsync(CatalogDbContext db)
    {
        db.GoverningBodyTypes.AddRange(
            new GoverningBodyType
            {
                Code = 130,
                Label = null,
                LabelVerified = false,
                IsNaturalPerson = false,
                Provenance = Fixture("synthetic governing body type"),
            },
            new GoverningBodyType
            {
                Code = 141,
                Label = null,
                LabelVerified = false,
                IsNaturalPerson = true,
                Provenance = Fixture("synthetic governing body type"),
            });

        db.Schools.AddRange(
            MakeSchool(1, SchoolA, "Szkola Podstawowa nr 1 w Testowie", GminaWithStreetList, "Gmina testowa pierwsza", 130),
            MakeSchool(2, SchoolB, "Szkola Podstawowa nr 2 w Testowie", GminaWithStreetList, "Gmina testowa pierwsza", 130),
            MakeSchool(3, SchoolC, "Szkola Podstawowa nr 3 w Testowie", GminaWithStreetList, "Gmina testowa pierwsza", 130),
            MakeSchool(4, SchoolD, "Szkola Podstawowa nr 4 w Testowie", GminaWithStreetList, "Gmina testowa pierwsza", 130),
            MakeSchool(5, SchoolE, "Szkola Podstawowa nr 5 w Testowie", GminaWithoutCatchmentData, "Gmina testowa druga", 130),
            MakeSchool(6, SchoolF, "Szkola Podstawowa nr 6 w Testowie", GminaWithoutCatchmentData, "Gmina testowa druga", 141),
            MakeSchool(7, SchoolG, "Szkola Podstawowa nr 7 w Testowie", GminaWithoutCatchmentData, "Gmina testowa druga", 130));

        // Headcount of the VIII class in the cohort year (the ClassVIIIHeadcount the engine receives).
        db.ClassCohorts.AddRange(
            MakeCohort(1, 20, 2), MakeCohort(2, 10, 1), MakeCohort(3, 12, 1), MakeCohort(4, 12, 1),
            MakeCohort(5, 5, 1), MakeCohort(6, 10, 1), MakeCohort(7, 15, 1));

        db.ExamResults.AddRange(
            MakeResult(1, 90, 10), MakeResult(2, 62, 10), MakeResult(3, 70, 12),
            MakeResult(4, 70, 12), MakeResult(5, 95, 5), MakeResult(6, 40, 10));

        db.NationalBaselines.Add(new NationalBaselineRow
        {
            ExamYear = ExamYear,
            Subject = "polish",
            NationalMeanPercent = 60.0,
            PupilStdDevPoints = 20.0,
            Provenance = Fixture("synthetic national baseline"),
        });

        db.CatchmentCoverages.Add(new CatchmentCoverage
        {
            GminaTeryt = GminaWithStreetList,
            State = "street_list",
            ResolutionUrl = StreetListResolutionUrl,
            ResolutionDate = new DateOnly(2024, 9, 1),
            Provenance = Fixture("synthetic catchment resolution"),
        });

        db.CatchmentStreets.AddRange(
            new CatchmentStreet
            {
                GminaTeryt = GminaWithStreetList,
                SchoolId = 2,
                StreetNormalized = "lipowa",
                NumberFrom = 1,
                NumberTo = 10,
                Parity = null,
                Provenance = Fixture("synthetic catchment resolution"),
            },
            new CatchmentStreet
            {
                GminaTeryt = GminaWithStreetList,
                SchoolId = 3,
                StreetNormalized = "dluga",
                NumberFrom = 1,
                NumberTo = 50,
                Parity = "even",
                Provenance = Fixture("synthetic catchment resolution"),
            });

        return Task.CompletedTask;
    }

    private static Provenance Fixture(string method) => new()
    {
        Source = "Synthetic test fixture",
        SourceUrl = "https://example.com/synthetic-fixture",
        AsOf = new DateOnly(2025, 6, 30),
        Method = method,
    };

    private static School MakeSchool(long id, string slug, string name, string gminaTeryt, string gminaName, int typeCode) => new()
    {
        IdPodmiot = id,
        Slug = slug,
        Name = name,
        GminaTeryt = gminaTeryt,
        GminaName = gminaName,
        GoverningBodyTypeCode = typeCode,
        IsPublic = true,
        IsSpecial = false,
        Provenance = Fixture("synthetic school register row"),
    };

    private static ClassCohort MakeCohort(long schoolId, int pupils, int oddzialy) => new()
    {
        SchoolId = schoolId,
        SchoolYear = CohortSchoolYear,
        ClassLevel = "VIII",
        Pupils = pupils,
        Oddzialy = oddzialy,
        Provenance = Fixture("synthetic cohort"),
    };

    private static ExamResult MakeResult(long schoolId, double meanPercent, int pupilsSat) => new()
    {
        SchoolId = schoolId,
        ExamYear = ExamYear,
        Subject = "polish",
        MeanPercent = meanPercent,
        PupilsSat = pupilsSat,
        Provenance = Fixture("synthetic exam result"),
    };
}
