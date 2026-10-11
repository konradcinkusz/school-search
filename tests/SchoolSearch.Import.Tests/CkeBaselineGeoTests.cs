using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Cke;
using SchoolSearch.Import.Geo;
using Xunit;

namespace SchoolSearch.Import.Tests;

/// <summary>
/// CKE, baseline and geo loaders against synthetic CSV text. CKE uses the UNVERIFIED <c>CkeColumnMap</c> defaults
/// (';' delimiter, one mean and one sat column per subject). Those defaults change with the real export.
/// </summary>
public sealed class CkeBaselineGeoTests
{
    private static readonly DateOnly FileDate = new(2026, 7, 1);

    private const string CkeCsv =
        "rspo;polish_mean;polish_sat;math_mean;math_sat;foreign_mean;foreign_sat\n" +
        "1001;62,5;11;48,0;11;;\n";

    private const string BaselineCsv =
        "subject,nationalMeanPercent,pupilStdDevPoints\n" +
        "polish,60.0,15.0\n" +
        "mathematics,50.0,18.0\n" +
        "foreign_language,70.0,14.0\n";

    private const string GeoCsv =
        "idPodmiot,lat,lon,source,sourceUrl,asOf,method\n" +
        "1001,52.2297,21.0122,test-coordinates,https://example.invalid/test-coordinates,2026-02-15,synthetic fixture\n";

    private static async Task SeedSchoolAsync(CatalogDbContext db, long id)
    {
        db.Schools.Add(new School
        {
            IdPodmiot = id,
            Name = "Szkola Podstawowa nr 1 w Testowie",
            Slug = "szkola-podstawowa-nr-1-testowie",
            IsPublic = true,
            Provenance = new Provenance
            {
                Source = "test seed",
                SourceUrl = "https://example.invalid/seed",
                AsOf = FileDate,
                Method = "synthetic fixture",
            },
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cke_running_twice_gives_identical_counts()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001);

        await CkeLoader.RunAsync(db, CsvStream.Of(CkeCsv), 2025, new CkeColumnMap(), TimeProvider.System, CancellationToken.None);
        var afterFirst = await TestDb.CountAsync(db);

        await CkeLoader.RunAsync(db, CsvStream.Of(CkeCsv), 2025, new CkeColumnMap(), TimeProvider.System, CancellationToken.None);
        var afterSecond = await TestDb.CountAsync(db);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(2, afterSecond.ExamResults);
    }

    [Fact]
    public async Task Cke_rows_carry_provenance()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001);

        await CkeLoader.RunAsync(db, CsvStream.Of(CkeCsv), 2025, new CkeColumnMap(), TimeProvider.System, CancellationToken.None);

        foreach (var result in await db.ExamResults.AsNoTracking().ToListAsync())
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Provenance.Source));
            Assert.False(string.IsNullOrWhiteSpace(result.Provenance.SourceUrl));
            Assert.False(string.IsNullOrWhiteSpace(result.Provenance.Method));
        }
    }

    [Fact]
    public async Task Cke_key_that_matches_no_school_is_rejected_and_counted_never_fuzzy_matched()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001);
        const string csv =
            "rspo;polish_mean;polish_sat;math_mean;math_sat;foreign_mean;foreign_sat\n" +
            "1001;62,5;11;;;;\n" +
            "9999;40,0;9;;;;\n";

        var report = await CkeLoader.RunAsync(db, CsvStream.Of(csv), 2025, new CkeColumnMap(), TimeProvider.System, CancellationToken.None);

        Assert.Equal(1, report.RowsRejected);
        Assert.Contains(report.RejectionReasons, reason => reason.Contains("9999", StringComparison.Ordinal));
        Assert.Equal(1, (await TestDb.CountAsync(db)).ExamResults);
        Assert.Empty(await db.ExamResults.Where(r => r.SchoolId == 9999).ToListAsync());
    }

    [Fact]
    public async Task Cke_file_with_imie_header_aborts()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001);
        const string csv =
            "rspo;imie;polish_mean;polish_sat;math_mean;math_sat;foreign_mean;foreign_sat\n" +
            "1001;TEST-VALUE;62,5;11;;;;\n";

        var exception = await Record.ExceptionAsync(() =>
            CkeLoader.RunAsync(db, CsvStream.Of(csv), 2025, new CkeColumnMap(), TimeProvider.System, CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Contains("imie", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, (await TestDb.CountAsync(db)).ExamResults);
    }

    [Fact]
    public async Task Baseline_running_twice_gives_identical_counts_and_provenance()
    {
        await using var db = TestDb.Create();
        var clock = FixedClock.OnDate(FileDate);

        await BaselineLoader.RunAsync(db, CsvStream.Of(BaselineCsv), 2025, clock, CancellationToken.None);
        var afterFirst = await TestDb.CountAsync(db);

        await BaselineLoader.RunAsync(db, CsvStream.Of(BaselineCsv), 2025, clock, CancellationToken.None);
        var afterSecond = await TestDb.CountAsync(db);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(3, afterSecond.NationalBaselines);
        foreach (var row in await db.NationalBaselines.AsNoTracking().ToListAsync())
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Provenance.Source));
            Assert.False(string.IsNullOrWhiteSpace(row.Provenance.SourceUrl));
            Assert.False(string.IsNullOrWhiteSpace(row.Provenance.Method));
        }
    }

    [Fact]
    public async Task Baseline_file_with_imie_header_aborts()
    {
        await using var db = TestDb.Create();
        const string csv =
            "subject,nationalMeanPercent,pupilStdDevPoints,imie\n" +
            "polish,60.0,15.0,TEST-VALUE\n";

        var exception = await Record.ExceptionAsync(() =>
            BaselineLoader.RunAsync(db, CsvStream.Of(csv), 2025, FixedClock.OnDate(FileDate), CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Contains("imie", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Geo_running_twice_gives_identical_counts_and_coordinate_provenance()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001);
        var clock = FixedClock.OnDate(FileDate);

        await GeoLoader.RunAsync(db, CsvStream.Of(GeoCsv), clock, CancellationToken.None);
        var afterFirst = await TestDb.CountAsync(db);

        await GeoLoader.RunAsync(db, CsvStream.Of(GeoCsv), clock, CancellationToken.None);
        var afterSecond = await TestDb.CountAsync(db);

        Assert.Equal(afterFirst, afterSecond);

        var school = await db.Schools.AsNoTracking().SingleAsync(s => s.IdPodmiot == 1001);
        Assert.NotNull(school.Latitude);
        var coordinates = school.CoordinatesProvenance;
        Assert.NotNull(coordinates);
        Assert.False(string.IsNullOrWhiteSpace(coordinates!.Source));
        Assert.False(string.IsNullOrWhiteSpace(coordinates.SourceUrl));
        Assert.False(string.IsNullOrWhiteSpace(coordinates.Method));
    }

    [Fact]
    public async Task Geo_file_with_imie_header_aborts()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001);
        const string csv =
            "idPodmiot,lat,lon,source,sourceUrl,asOf,method,imie\n" +
            "1001,52.2297,21.0122,test,https://example.invalid/t,2026-02-15,synthetic,TEST-VALUE\n";

        var exception = await Record.ExceptionAsync(() =>
            GeoLoader.RunAsync(db, CsvStream.Of(csv), FixedClock.OnDate(FileDate), CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Contains("imie", exception.Message, StringComparison.Ordinal);
    }
}
