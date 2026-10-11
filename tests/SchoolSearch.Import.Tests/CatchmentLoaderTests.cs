using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import;
using SchoolSearch.Import.Catchment;
using Xunit;

namespace SchoolSearch.Import.Tests;

/// <summary>
/// Catchment CSV loader and street normaliser. All gmina codes and street names are fictional: the TERYT
/// voivodeship prefix 99 does not exist, and "ul. Przykładowa" is a placeholder name.
/// </summary>
public sealed class CatchmentLoaderTests
{
    private const string GminaA = "9900001";
    private const string GminaB = "9900002";
    private const string GminaNone = "9900003";

    private const string Header =
        "gminaTeryt,schoolIdPodmiot,street,numberFrom,numberTo,parity,state,resolutionUrl,resolutionDate,source,sourceUrl,asOf,method\n";

    /// <summary>The coverage columns shared by every street row of a gmina (state through method).</summary>
    private const string SharedTail =
        "street_list,https://example.invalid/uchwala-test,2026-01-15,synthetic resolution,https://example.invalid/uchwala-test,2026-09-01,synthetic fixture";

    private static readonly TimeProvider Clock = FixedClock.OnDate(new DateOnly(2026, 10, 1));

    private static string Row(string gmina, string school, string street, string from = "", string to = "", string parity = "") =>
        $"{gmina},{school},{street},{from},{to},{parity},{SharedTail}\n";

    private static async Task SeedSchoolAsync(CatalogDbContext db, long id, string name)
    {
        db.Schools.Add(new School
        {
            IdPodmiot = id,
            Name = name,
            Slug = $"catchment-test-{id}",
            IsPublic = true,
            GminaTeryt = GminaA,
            Provenance = new Provenance
            {
                Source = "test seed",
                SourceUrl = "https://example.invalid/seed",
                AsOf = new DateOnly(2026, 9, 1),
                Method = "synthetic fixture",
            },
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Stable text view of the catchment tables (ids excluded: identity values change on every run).</summary>
    private static async Task<string> SnapshotAsync(CatalogDbContext db)
    {
        var streets = (await db.CatchmentStreets.AsNoTracking().ToListAsync())
            .OrderBy(c => c.GminaTeryt, StringComparer.Ordinal)
            .ThenBy(c => c.SchoolId)
            .ThenBy(c => c.StreetNormalized, StringComparer.Ordinal)
            .Select(c => $"{c.GminaTeryt}|{c.SchoolId}|{c.StreetNormalized}|{c.NumberFrom}|{c.NumberTo}|{c.Parity}|{c.Provenance.Source}|{c.Provenance.AsOf:yyyy-MM-dd}");

        var coverages = (await db.CatchmentCoverages.AsNoTracking().ToListAsync())
            .OrderBy(c => c.GminaTeryt, StringComparer.Ordinal)
            .Select(c => $"{c.GminaTeryt}|{c.State}|{c.ResolutionUrl}|{c.ResolutionDate:yyyy-MM-dd}|{c.Provenance.AsOf:yyyy-MM-dd}");

        return string.Join("\n", streets) + "\n--\n" + string.Join("\n", coverages);
    }

    [Fact]
    public async Task Running_twice_gives_identical_rows_and_counts()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001, "Szkola Podstawowa nr 1 w Testowie");
        await SeedSchoolAsync(db, 1002, "Szkola Podstawowa nr 2 w Testowie");

        var csv = Header + Row(GminaA, "1001", "ul. Przykładowa") + Row(GminaA, "1002", "ul. Testowa", "1", "20", "odd");

        var first = await CatchmentLoader.RunAsync(db, CsvStream.Of(csv), Clock, CancellationToken.None);
        var afterFirst = await SnapshotAsync(db);

        var second = await CatchmentLoader.RunAsync(db, CsvStream.Of(csv), Clock, CancellationToken.None);
        var afterSecond = await SnapshotAsync(db);

        Assert.Equal(2, first.RowsRead);
        Assert.Equal(2, first.RowsWritten);
        Assert.Equal(0, first.RowsRejected);
        Assert.Equal(first.RowsWritten, second.RowsWritten);
        Assert.Equal(first.RowsRejected, second.RowsRejected);
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(2, await db.CatchmentStreets.CountAsync());
        Assert.Equal(1, await db.CatchmentCoverages.CountAsync());
    }

    [Fact]
    public async Task Unknown_school_id_is_rejected_and_its_gmina_is_not_written()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001, "Szkola Podstawowa nr 1 w Testowie");

        var csv = Header
            + Row(GminaA, "1001", "ul. Przykładowa")
            + Row(GminaB, "1001", "ul. Testowa")
            + Row(GminaB, "9999", "ul. Ogrodowa");

        var report = await CatchmentLoader.RunAsync(db, CsvStream.Of(csv), Clock, CancellationToken.None);

        Assert.Equal(3, report.RowsRead);
        Assert.Equal(1, report.RowsWritten);
        Assert.Equal(2, report.RowsRejected);
        Assert.Contains(report.RejectionReasons, reason => reason.Contains("9999", StringComparison.Ordinal) && reason.Contains("matches no School", StringComparison.Ordinal));
        Assert.Contains(report.RejectionReasons, reason => reason.Contains(GminaB, StringComparison.Ordinal) && reason.Contains("nothing written", StringComparison.Ordinal));

        Assert.Single(await db.CatchmentStreets.Where(c => c.GminaTeryt == GminaA).ToListAsync());
        Assert.Empty(await db.CatchmentStreets.Where(c => c.GminaTeryt == GminaB).ToListAsync());
        Assert.Empty(await db.CatchmentCoverages.Where(c => c.GminaTeryt == GminaB).ToListAsync());
    }

    [Fact]
    public async Task Rejected_gmina_keeps_its_previously_loaded_streets()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001, "Szkola Podstawowa nr 1 w Testowie");

        await CatchmentLoader.RunAsync(db, CsvStream.Of(Header + Row(GminaB, "1001", "ul. Testowa")), Clock, CancellationToken.None);
        await CatchmentLoader.RunAsync(db, CsvStream.Of(Header + Row(GminaB, "9999", "ul. Testowa")), Clock, CancellationToken.None);

        var streets = await db.CatchmentStreets.AsNoTracking().Where(c => c.GminaTeryt == GminaB).ToListAsync();
        var single = Assert.Single(streets);
        Assert.Equal("testowa", single.StreetNormalized);
        Assert.Equal(1001, single.SchoolId);
    }

    [Fact]
    public async Task A_second_file_replaces_the_streets_of_a_gmina()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001, "Szkola Podstawowa nr 1 w Testowie");

        await CatchmentLoader.RunAsync(
            db,
            CsvStream.Of(Header + Row(GminaA, "1001", "ul. Przykładowa") + Row(GminaA, "1001", "ul. Ogrodowa")),
            Clock,
            CancellationToken.None);

        await CatchmentLoader.RunAsync(
            db,
            CsvStream.Of(Header + Row(GminaA, "1001", "ul. Ogrodowa")),
            Clock,
            CancellationToken.None);

        var streets = await db.CatchmentStreets.AsNoTracking().ToListAsync();
        var single = Assert.Single(streets);
        Assert.Equal("ogrodowa", single.StreetNormalized);
    }

    [Fact]
    public async Task State_none_row_records_coverage_without_streets()
    {
        await using var db = TestDb.Create();

        var csv = Header
            + "9900003,,,,,,none,,,synthetic source,https://example.invalid/none,2026-09-01,synthetic fixture\n";

        var report = await CatchmentLoader.RunAsync(db, CsvStream.Of(csv), Clock, CancellationToken.None);

        Assert.Equal(1, report.RowsWritten);
        Assert.Equal(0, report.RowsRejected);
        var coverage = await db.CatchmentCoverages.AsNoTracking().SingleAsync(c => c.GminaTeryt == GminaNone);
        Assert.Equal("none", coverage.State);
        Assert.Equal(new DateOnly(2026, 9, 1), coverage.Provenance.AsOf);
        Assert.Empty(await db.CatchmentStreets.ToListAsync());
    }

    [Fact]
    public async Task Missing_column_aborts_before_anything_is_written()
    {
        await using var db = TestDb.Create();
        await SeedSchoolAsync(db, 1001, "Szkola Podstawowa nr 1 w Testowie");

        var headerWithoutParity = "gminaTeryt,schoolIdPodmiot,street,numberFrom,numberTo,state,resolutionUrl,resolutionDate,source,sourceUrl,asOf,method\n";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CatchmentLoader.RunAsync(db, CsvStream.Of(headerWithoutParity + "9900001,1001,ul. X,,,street_list,https://example.invalid/u,,s,https://example.invalid/s,2026-09-01,m\n"), Clock, CancellationToken.None));

        Assert.Contains("parity", error.Message, StringComparison.Ordinal);
        Assert.Empty(await db.CatchmentStreets.ToListAsync());
        Assert.Empty(await db.CatchmentCoverages.ToListAsync());
    }

    [Fact]
    public async Task Personal_data_column_aborts_the_whole_file()
    {
        await using var db = TestDb.Create();

        var csvWithName = "gminaTeryt,nazwisko,schoolIdPodmiot,street,numberFrom,numberTo,parity,state,resolutionUrl,resolutionDate,source,sourceUrl,asOf,method\n";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CatchmentLoader.RunAsync(db, CsvStream.Of(csvWithName), Clock, CancellationToken.None));

        Assert.Empty(await db.CatchmentStreets.ToListAsync());
    }

    [Theory]
    [InlineData("ul. Przykładowa", "przykladowa")]
    [InlineData("ULICA PRZYKŁADOWA", "przykladowa")]
    [InlineData("Al.  Łąkowa", "lakowa")]
    [InlineData("aleja Żółta", "zolta")]
    [InlineData("pl. Ćwiczeń", "cwiczen")]
    [InlineData("plac Wolności", "wolnosci")]
    [InlineData("  Ogrodowa   ", "ogrodowa")]
    [InlineData("ul.Przykładowa", "przykladowa")]
    [InlineData("Jana Pawła II", "jana pawla ii")]
    [InlineData("Marii Skłodowskiej-Curie", "marii sklodowskiej curie")]
    [InlineData("Ulicowa", "ulicowa")]
    [InlineData("Ślązaków", "slazakow")]
    [InlineData("ul.", "")]
    [InlineData("", "")]
    public void Street_normaliser_matches_the_golden_list(string raw, string expected)
    {
        Assert.Equal(expected, StreetNormalizer.Normalize(raw));
    }
}
