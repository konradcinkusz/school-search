using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Demo;

/// <summary>
/// Clearly synthetic demo data for the fictional gmina "Przykładowo" (TERYT 9999999). Nothing here
/// describes a real school, pupil, street or exam. Every row carries the provenance
/// <see cref="Source"/> = "DEMO SEED (synthetic)" so it cannot be mistaken for published data.
/// </summary>
/// <remarks>
/// Idempotent: each row is inserted only when its natural key is missing, so restarts never duplicate
/// rows and never overwrite a row someone changed. Runs only through <see cref="DemoSeedHostedService"/>,
/// which refuses to run in Production.
/// </remarks>
public static class DemoSeed
{
    public const string Source = "DEMO SEED (synthetic)";
    public const string SourceUrl = "https://example.invalid/demo";
    public const string Method = "synthetic";

    public const string GminaTeryt = "9999999";
    public const string GminaName = "Przykładowo";
    public const string SecondGminaTeryt = "9999998";
    public const string SecondGminaName = "Przykładowo-Wschód";

    public const int FirstExamYear = 2024;
    public const int SecondExamYear = 2025;

    public static readonly DateOnly AsOf = new(2026, 1, 1);

    private const string ClassLevel = "VIII";
    private const double BaselineMean = 60.0;
    private const double BaselineStdDevPoints = 20.0;

    /// <summary>One exam year for one school. Means are percent points per subject.</summary>
    private sealed record Exam(int Sat, double Polish, double Mathematics, double ForeignLanguage);

    /// <summary>
    /// One synthetic school. <see cref="Pupils"/> is the VIII-class headcount used for both cohort years.
    /// A null exam means no exam data for that year; both null means the school has no exam data at all.
    /// </summary>
    private sealed record SchoolSpec(
        long IdPodmiot,
        string Slug,
        string Name,
        string Teryt,
        string Gmina,
        bool IsPublic,
        bool IsSpecial,
        int Pupils,
        double Latitude,
        double Longitude,
        Exam? Exam2024,
        Exam? Exam2025);

    private static readonly SchoolSpec[] Schools =
    [
        // Large cohort, public. Catchment school for ul. Przykładowa 1-20.
        new(900001, "demo-sp-1", "Szkoła Podstawowa nr 1 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 120, 52.0010, 19.0010,
            new Exam(118, 72.5, 61.0, 80.0), new Exam(117, 71.0, 63.0, 78.5)),

        // Exactly 9 sat: the lowest published cohort (threshold 9).
        new(900002, "demo-sp-2", "Szkoła Podstawowa nr 2 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 9, 52.0020, 19.0020,
            new Exam(9, 64.0, 58.0, 70.0), new Exam(9, 66.0, 60.0, 72.0)),

        // 8 sat: inside the limited band (5 to 8, ADR-0009), must read limited with no numeric score and no rank.
        new(900003, "demo-sp-3", "Szkoła Podstawowa nr 3 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 8, 52.0030, 19.0030,
            new Exam(8, 50.0, 48.0, 55.0), new Exam(8, 52.0, 49.0, 56.0)),

        // Counter-metric case: low coverage (about one third sat) with a clearly higher average.
        new(900004, "demo-sp-4", "Szkoła Podstawowa nr 4 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 60, 52.0040, 19.0040,
            new Exam(20, 85.0, 82.0, 88.0), new Exam(21, 86.0, 83.0, 89.0)),

        // Non-public school.
        new(900005, "demo-sp-5", "Szkoła Podstawowa nr 5 w Przykładowie (niepubliczna, dane demonstracyjne)", GminaTeryt, GminaName, false, false, 45, 52.0050, 19.0050,
            new Exam(40, 58.0, 55.0, 60.0), new Exam(41, 57.0, 54.0, 61.0)),

        // No exam data at all: cohorts only.
        new(900006, "demo-sp-6", "Szkoła Podstawowa nr 6 w Przykładowie (bez danych egzaminacyjnych, dane demonstracyjne)", GminaTeryt, GminaName, true, false, 50, 52.0060, 19.0060,
            null, null),

        // Ordinary public schools for ranking and comparison. Catchment school for ul. Testowa.
        new(900007, "demo-sp-7", "Szkoła Podstawowa nr 7 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 100, 52.0070, 19.0070,
            new Exam(96, 70.0, 68.0, 74.0), new Exam(95, 71.0, 69.0, 75.0)),
        new(900008, "demo-sp-8", "Szkoła Podstawowa nr 8 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 75, 52.0080, 19.0080,
            new Exam(70, 64.0, 60.0, 66.0), new Exam(72, 63.0, 61.0, 65.0)),
        new(900009, "demo-sp-9", "Szkoła Podstawowa nr 9 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 50, 52.0090, 19.0090,
            new Exam(49, 55.0, 52.0, 58.0), new Exam(48, 56.0, 53.0, 59.0)),

        // Special-education school.
        new(900010, "demo-sp-10", "Szkoła Podstawowa specjalna nr 10 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, true, 25, 52.0100, 19.0100,
            new Exam(24, 48.0, 45.0, 50.0), new Exam(24, 47.0, 46.0, 51.0)),

        new(900011, "demo-sp-11", "Szkoła Podstawowa nr 11 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 90, 52.0110, 19.0110,
            new Exam(88, 58.0, 55.0, 62.0), new Exam(87, 60.0, 57.0, 63.0)),
        new(900012, "demo-sp-12", "Szkoła Podstawowa nr 12 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 40, 52.0120, 19.0120,
            new Exam(39, 61.0, 59.0, 63.0), new Exam(38, 62.0, 60.0, 64.0)),
        new(900013, "demo-sp-13", "Szkoła Podstawowa nr 13 w Przykładowie (dane demonstracyjne)", GminaTeryt, GminaName, true, false, 33, 52.0130, 19.0130,
            new Exam(31, 66.0, 64.0, 69.0), new Exam(31, 67.0, 65.0, 70.0)),

        // Second gmina with no catchment coverage: the explicit "no data" case.
        new(900014, "demo-sp-14", "Szkoła Podstawowa w Przykładowie-Wschód (dane demonstracyjne)", SecondGminaTeryt, SecondGminaName, true, false, 30, 52.1000, 19.1000,
            new Exam(29, 60.0, 58.0, 61.0), new Exam(29, 59.0, 57.0, 62.0)),
    ];

    /// <summary>Catchment rows for the fictional gmina. Street names are normalized (lower-case, diacritics folded).</summary>
    private sealed record StreetSpec(string StreetNormalized, int? NumberFrom, int? NumberTo, long SchoolId);

    private static readonly StreetSpec[] Streets =
    [
        new("przykladowa", 1, 20, 900001),
        new("przykladowa", 21, 40, 900002),
        new("testowa", null, null, 900007),
    ];

    /// <summary>The demo rows, built in memory. Pure: no database access.</summary>
    public static DemoSeedSet Build()
    {
        var schools = new List<School>();
        var cohorts = new List<ClassCohort>();
        var results = new List<ExamResult>();

        foreach (var spec in Schools)
        {
            schools.Add(new School
            {
                IdPodmiot = spec.IdPodmiot,
                Name = spec.Name,
                Slug = spec.Slug,
                GminaTeryt = spec.Teryt,
                GminaName = spec.Gmina,
                IsPublic = spec.IsPublic,
                IsSpecial = spec.IsSpecial,
                Latitude = spec.Latitude,
                Longitude = spec.Longitude,
                CoordinatesProvenance = Prov(),
                Provenance = Prov(),
            });

            // School year Y-1 holds the cohort for exam year Y (ExamYear convention, SPEC-C).
            foreach (var schoolYear in new[] { FirstExamYear - 1, SecondExamYear - 1 })
            {
                cohorts.Add(new ClassCohort
                {
                    SchoolId = spec.IdPodmiot,
                    SchoolYear = schoolYear,
                    ClassLevel = ClassLevel,
                    Pupils = spec.Pupils,
                    Oddzialy = spec.Pupils >= 50 ? 3 : 1,
                    Provenance = Prov(),
                });
            }

            AddExam(results, spec, FirstExamYear, spec.Exam2024);
            AddExam(results, spec, SecondExamYear, spec.Exam2025);
        }

        var baselines = new List<NationalBaselineRow>();
        foreach (var examYear in new[] { FirstExamYear, SecondExamYear })
        {
            foreach (var subject in Subjects)
            {
                baselines.Add(new NationalBaselineRow
                {
                    ExamYear = examYear,
                    Subject = subject,
                    NationalMeanPercent = BaselineMean,
                    PupilStdDevPoints = BaselineStdDevPoints,
                    Provenance = Prov(),
                });
            }
        }

        var coverages = new List<CatchmentCoverage>
        {
            new()
            {
                GminaTeryt = GminaTeryt,
                State = "street_list",
                ResolutionUrl = "https://example.invalid/demo-uchwala",
                ResolutionDate = new DateOnly(2025, 9, 1),
                Provenance = Prov(),
            },
        };

        var streets = Streets.Select(s => new CatchmentStreet
        {
            GminaTeryt = GminaTeryt,
            SchoolId = s.SchoolId,
            StreetNormalized = s.StreetNormalized,
            NumberFrom = s.NumberFrom,
            NumberTo = s.NumberTo,
            Parity = null,
            Provenance = Prov(),
        }).ToList();

        return new DemoSeedSet(schools, cohorts, results, baselines, coverages, streets);
    }

    /// <summary>Inserts every demo row whose natural key is missing. Safe to run any number of times.</summary>
    public static async Task<DemoSeedReport> ApplyAsync(CatalogDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var set = Build();
        var added = 0;

        foreach (var school in set.Schools)
        {
            if (!await db.Schools.AnyAsync(s => s.IdPodmiot == school.IdPodmiot, cancellationToken))
            {
                db.Schools.Add(school);
                added++;
            }
        }

        foreach (var cohort in set.Cohorts)
        {
            if (!await db.ClassCohorts.AnyAsync(c =>
                    c.SchoolId == cohort.SchoolId && c.SchoolYear == cohort.SchoolYear && c.ClassLevel == cohort.ClassLevel,
                    cancellationToken))
            {
                db.ClassCohorts.Add(cohort);
                added++;
            }
        }

        foreach (var result in set.ExamResults)
        {
            if (!await db.ExamResults.AnyAsync(e =>
                    e.SchoolId == result.SchoolId && e.ExamYear == result.ExamYear && e.Subject == result.Subject,
                    cancellationToken))
            {
                db.ExamResults.Add(result);
                added++;
            }
        }

        foreach (var baseline in set.Baselines)
        {
            if (!await db.NationalBaselines.AnyAsync(n => n.ExamYear == baseline.ExamYear && n.Subject == baseline.Subject, cancellationToken))
            {
                db.NationalBaselines.Add(baseline);
                added++;
            }
        }

        foreach (var coverage in set.Coverages)
        {
            if (!await db.CatchmentCoverages.AnyAsync(c => c.GminaTeryt == coverage.GminaTeryt, cancellationToken))
            {
                db.CatchmentCoverages.Add(coverage);
                added++;
            }
        }

        foreach (var street in set.Streets)
        {
            if (!await db.CatchmentStreets.AnyAsync(s =>
                    s.GminaTeryt == street.GminaTeryt
                    && s.SchoolId == street.SchoolId
                    && s.StreetNormalized == street.StreetNormalized
                    && s.NumberFrom == street.NumberFrom
                    && s.NumberTo == street.NumberTo,
                    cancellationToken))
            {
                db.CatchmentStreets.Add(street);
                added++;
            }
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new DemoSeedReport(added);
    }

    private static readonly string[] Subjects = ["polish", "mathematics", "foreign_language"];

    private static void AddExam(List<ExamResult> results, SchoolSpec spec, int examYear, Exam? exam)
    {
        if (exam is null)
        {
            return;
        }

        results.Add(Result(spec.IdPodmiot, examYear, "polish", exam.Polish, exam.Sat));
        results.Add(Result(spec.IdPodmiot, examYear, "mathematics", exam.Mathematics, exam.Sat));
        results.Add(Result(spec.IdPodmiot, examYear, "foreign_language", exam.ForeignLanguage, exam.Sat));
    }

    private static ExamResult Result(long schoolId, int examYear, string subject, double mean, int sat) => new()
    {
        SchoolId = schoolId,
        ExamYear = examYear,
        Subject = subject,
        MeanPercent = mean,
        PupilsSat = sat,
        Provenance = Prov(),
    };

    private static Provenance Prov() => new()
    {
        Source = Source,
        SourceUrl = SourceUrl,
        AsOf = AsOf,
        Method = Method,
    };
}

/// <summary>The demo rows as entities, before they are compared with the database.</summary>
public sealed record DemoSeedSet(
    IReadOnlyList<School> Schools,
    IReadOnlyList<ClassCohort> Cohorts,
    IReadOnlyList<ExamResult> ExamResults,
    IReadOnlyList<NationalBaselineRow> Baselines,
    IReadOnlyList<CatchmentCoverage> Coverages,
    IReadOnlyList<CatchmentStreet> Streets);

/// <summary>Outcome of one seed run: rows inserted (zero on a repeat run).</summary>
public sealed record DemoSeedReport(int RowsAdded);
