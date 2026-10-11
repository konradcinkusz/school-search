using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Preschools;
using Xunit;

namespace SchoolSearch.Import.Tests;

/// <summary>Preschool SIO and facts loaders against synthetic CSV text. Names and addresses are invented.</summary>
public sealed class PreschoolLoaderTests
{
    private static readonly DateOnly FileDate = new(2026, 9, 1);

    private const string SioHeader =
        "idPodmiot;TypPodmiotu;Nazwa;TerytGmina;Gmina;Publiczność;idTypOrgProw;Ulica;KodPocztowy;Miejscowosc";

    private const string SioCsv = SioHeader + "\n" +
        "9001;Przedszkole;Przedszkole Testowe nr 1;1465011;Gmina Testowa;publiczna;140;ul. Testowa 1;00-001;Testowo\n" +
        "9002;Przedszkole;Przedszkole Testowe nr 2;1465011;Gmina Testowa;niepubliczna;141;ul. Testowa 2;00-002;Testowo\n" +
        "9003;Szkola podstawowa;Szkola Testowa;1465011;Gmina Testowa;publiczna;130;ul. Testowa 3;00-003;Testowo\n" +
        "9004;Przedszkole;;1465011;Gmina Testowa;publiczna;130;ul. Testowa 4;00-004;Testowo\n" +
        "abc;Przedszkole;Przedszkole Zle;1465011;Gmina Testowa;publiczna;130;ul. Testowa 5;00-005;Testowo\n";

    private const string FactsHeader = "idPodmiot;tuitionMin;tuitionMax;meal;opens;closes;freePlaces;freePlacesAsOf;confidence";

    private static Task<ImportReport> RunSioAsync(CatalogDbContext db, string csv) =>
        PreschoolSioLoader.RunAsync(db, CsvStream.Of(csv), new PreschoolSioColumnMap(), TimeProvider.System, CancellationToken.None, FileDate);

    private static Task<ImportReport> RunFactsAsync(CatalogDbContext db, string csv) =>
        PreschoolFactsLoader.RunAsync(
            db,
            CsvStream.Of(csv),
            new PreschoolFactsSource("Test gmina tuition table", "https://example.invalid/tuition", FileDate),
            CancellationToken.None);

    private static async Task SeedPreschoolsAsync(CatalogDbContext db, params long[] ids)
    {
        foreach (var id in ids)
        {
            db.Preschools.Add(new Preschool
            {
                IdPodmiot = id,
                Name = $"Przedszkole Seed {id}",
                Slug = $"przedszkole-seed-{id}",
                IsPublic = true,
                Provenance = new Provenance
                {
                    Source = "test seed",
                    SourceUrl = "https://example.invalid/seed",
                    AsOf = FileDate,
                    Method = "synthetic fixture",
                },
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sio_loads_preschool_units_and_skips_other_types()
    {
        await using var db = TestDb.Create();

        var report = await RunSioAsync(db, SioCsv);

        var preschools = await db.Preschools.AsNoTracking().OrderBy(p => p.IdPodmiot).ToListAsync();
        Assert.Equal(new long[] { 9001, 9002 }, preschools.Select(p => p.IdPodmiot));
        Assert.Equal(5, report.RowsRead);
        Assert.Equal(2, report.RowsWritten);
        Assert.Equal(2, report.RowsRejected);
    }

    [Fact]
    public async Task Sio_stores_public_flag_type_code_slug_and_provenance()
    {
        await using var db = TestDb.Create();

        await RunSioAsync(db, SioCsv);

        var public1 = await db.Preschools.SingleAsync(p => p.IdPodmiot == 9001);
        Assert.True(public1.IsPublic);
        Assert.Equal(140, public1.GoverningBodyTypeCode);
        Assert.Equal("przedszkole-testowe-nr-1-9001", public1.Slug);
        Assert.Equal("1465011", public1.GminaTeryt);
        Assert.Equal("Gmina Testowa", public1.GminaName);
        Assert.Equal("ul. Testowa 1", public1.Street);
        Assert.Equal("00-001", public1.PostalCode);
        Assert.Equal("Testowo", public1.City);
        Assert.Equal("SIO", public1.Provenance.Source);
        Assert.Equal("csv import", public1.Provenance.Method);
        Assert.Equal(FileDate, public1.Provenance.AsOf);

        var nonPublic = await db.Preschools.SingleAsync(p => p.IdPodmiot == 9002);
        Assert.False(nonPublic.IsPublic);
    }

    [Fact]
    public async Task Sio_running_twice_gives_identical_counts()
    {
        await using var db = TestDb.Create();

        await RunSioAsync(db, SioCsv);
        var afterFirst = await db.Preschools.CountAsync();

        await RunSioAsync(db, SioCsv);
        var afterSecond = await db.Preschools.CountAsync();

        Assert.Equal(2, afterFirst);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task Sio_missing_mapped_column_aborts_and_writes_nothing()
    {
        await using var db = TestDb.Create();
        var csvWithoutName = SioCsv.Replace(";Nazwa", ";Nazwa-missing", StringComparison.Ordinal);

        var exception = await Record.ExceptionAsync(() => RunSioAsync(db, csvWithoutName));

        Assert.NotNull(exception);
        Assert.Contains("Nazwa", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Preschools.CountAsync());
    }

    [Fact]
    public async Task Sio_personal_data_column_aborts_the_whole_file()
    {
        await using var db = TestDb.Create();
        var csvWithName = SioHeader + ";imie\n" + "9001;Przedszkole;Przedszkole Testowe nr 1;1465011;Gmina Testowa;publiczna;140;ul. Testowa 1;00-001;Testowo;TEST-VALUE\n";

        var exception = await Record.ExceptionAsync(() => RunSioAsync(db, csvWithName));

        Assert.NotNull(exception);
        Assert.Contains("imie", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, await db.Preschools.CountAsync());
    }

    [Fact]
    public async Task Facts_load_decimal_comma_times_and_free_places_for_known_preschool()
    {
        await using var db = TestDb.Create();
        await SeedPreschoolsAsync(db, 9001);
        const string csv = FactsHeader + "\n" +
            "9001;450,50;520;90;07:00;16:30;3;2026-09-01;reported\n";

        var report = await RunFactsAsync(db, csv);

        var facts = await db.PreschoolFacts.SingleAsync(f => f.PreschoolId == 9001);
        Assert.Equal(450.50m, facts.MonthlyTuitionMinPln);
        Assert.Equal(520m, facts.MonthlyTuitionMaxPln);
        Assert.Equal(90m, facts.MonthlyMealPln);
        Assert.Equal(new TimeOnly(7, 0), facts.OpensAt);
        Assert.Equal(new TimeOnly(16, 30), facts.ClosesAt);
        Assert.Equal(3, facts.FreePlaces);
        Assert.Equal(FileDate, facts.FreePlacesAsOf);
        Assert.Equal("reported", facts.Confidence);
        Assert.Equal("Test gmina tuition table", facts.Provenance.Source);
        Assert.Equal("https://example.invalid/tuition", facts.Provenance.SourceUrl);
        Assert.Equal("csv import", facts.Provenance.Method);
        Assert.Equal(1, report.RowsWritten);
        Assert.Equal(0, report.RowsRejected);
    }

    [Fact]
    public async Task Facts_reject_unknown_ids_and_bad_rows_but_keep_the_good_ones()
    {
        await using var db = TestDb.Create();
        await SeedPreschoolsAsync(db, 9001, 9002, 9003);
        const string csv = FactsHeader + "\n" +
            "9001;450;520;;;;;;reported\n" +
            "9002;600;500;;;;;;\n" +
            "9999;300;300;;;;;;\n" +
            "9003;;;;;3;;reported\n";

        var report = await RunFactsAsync(db, csv);

        Assert.Equal(1, await db.PreschoolFacts.CountAsync());
        Assert.NotNull(await db.PreschoolFacts.SingleOrDefaultAsync(f => f.PreschoolId == 9001));
        Assert.Equal(1, report.RowsWritten);
        Assert.Equal(3, report.RowsRejected);
        Assert.Contains(report.RejectionReasons, r => r.Contains("above", StringComparison.Ordinal));
        Assert.Contains(report.RejectionReasons, r => r.Contains("matches no preschool", StringComparison.Ordinal));
        Assert.Contains(report.RejectionReasons, r => r.Contains("freePlacesAsOf", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Facts_reject_money_with_three_decimals_closing_before_opening_and_bad_confidence()
    {
        await using var db = TestDb.Create();
        await SeedPreschoolsAsync(db, 9001, 9002, 9003);
        const string csv = FactsHeader + "\n" +
            "9001;450,505;;;;;;;reported\n" +
            "9002;;;;16:00;07:00;;;reported\n" +
            "9003;450;;;;;;;guessed\n";

        var report = await RunFactsAsync(db, csv);

        Assert.Equal(0, await db.PreschoolFacts.CountAsync());
        Assert.Equal(3, report.RowsRejected);
    }

    [Fact]
    public async Task Facts_running_twice_gives_identical_counts()
    {
        await using var db = TestDb.Create();
        await SeedPreschoolsAsync(db, 9001);
        const string csv = FactsHeader + "\n" + "9001;450;520;;;;;;\n";

        await RunFactsAsync(db, csv);
        await RunFactsAsync(db, csv);

        Assert.Equal(1, await db.PreschoolFacts.CountAsync());
    }

    [Fact]
    public async Task Facts_source_arguments_are_refused_when_not_absolute_urls()
    {
        await using var db = TestDb.Create();
        await SeedPreschoolsAsync(db, 9001);

        var exception = await Record.ExceptionAsync(() => PreschoolFactsLoader.RunAsync(
            db,
            CsvStream.Of(FactsHeader + "\n" + "9001;450;;;;;;;\n"),
            new PreschoolFactsSource("Test", "not a url", FileDate),
            CancellationToken.None));

        Assert.IsType<ArgumentException>(exception);
        Assert.Equal(0, await db.PreschoolFacts.CountAsync());
    }
}
