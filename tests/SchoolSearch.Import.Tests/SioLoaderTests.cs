using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Sio;
using Xunit;

namespace SchoolSearch.Import.Tests;

public sealed class SioLoaderTests
{
    private static readonly DateOnly FileDate = new(2026, 2, 15);

    private static Task<ImportReport> RunAsync(CatalogDbContext db, MemoryStream xlsx) =>
        SioLoader.RunAsync(
            db,
            xlsx,
            2025,
            new SioColumnMap(),
            FixedClock.OnDate(FileDate),
            CancellationToken.None);

    [Fact]
    public async Task Running_twice_gives_identical_row_counts_and_values()
    {
        await using var db = TestDb.Create();
        var rows = new[]
        {
            SioFixtures.School("1001", "Szkola Podstawowa nr 1 w Testowie", "141"),
            SioFixtures.School("1002", "Szkola Podstawowa nr 2 w Testowie", "140"),
        };

        await RunAsync(db, SyntheticXlsx.Build(SioFixtures.Headers, rows));
        var afterFirst = await TestDb.CountAsync(db);

        await RunAsync(db, SyntheticXlsx.Build(SioFixtures.Headers, rows));
        var afterSecond = await TestDb.CountAsync(db);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(2, afterSecond.Schools);
    }

    [Fact]
    public async Task Every_written_row_carries_non_empty_provenance()
    {
        await using var db = TestDb.Create();
        var rows = new[] { SioFixtures.School("1001", "Szkola Podstawowa nr 1 w Testowie", "130") };

        await RunAsync(db, SyntheticXlsx.Build(SioFixtures.Headers, rows));

        var schools = await db.Schools.AsNoTracking().ToListAsync();
        Assert.NotEmpty(schools);
        foreach (var school in schools)
        {
            AssertProvenance(school.Provenance, FileDate);
        }

        var cohorts = await db.ClassCohorts.AsNoTracking().ToListAsync();
        Assert.NotEmpty(cohorts);
        foreach (var cohort in cohorts)
        {
            AssertProvenance(cohort.Provenance, FileDate);
        }
    }

    [Fact]
    public async Task Natural_person_governing_body_is_stored_as_its_type_code_only()
    {
        await using var db = TestDb.Create();
        var rows = new[] { SioFixtures.School("1001", "Szkola Podstawowa nr 1 w Testowie", "141") };

        await RunAsync(db, SyntheticXlsx.Build(SioFixtures.Headers, rows));

        var school = await db.Schools.SingleAsync(s => s.IdPodmiot == 1001);
        Assert.Equal(141, school.GoverningBodyTypeCode);
    }

    [Fact]
    public async Task No_entity_property_can_hold_an_operator_name()
    {
        var catalogTypes = typeof(School).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("SchoolSearch.Catalog.Data", StringComparison.Ordinal))
            .ToList();

        var offending = catalogTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.Name.Contains("Operator", StringComparison.OrdinalIgnoreCase)
                || p.Name.Contains("OrganProwadz", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.DeclaringType!.Name + "." + p.Name)
            .ToList();

        Assert.Empty(offending);
        Assert.NotNull(typeof(School).GetProperty(nameof(School.GoverningBodyTypeCode)));
    }

    [Fact]
    public async Task Organ_name_column_is_never_stored()
    {
        await using var db = TestDb.Create();
        var rows = new[] { SioFixtures.School("1001", "Szkola Podstawowa nr 1 w Testowie", "130") };

        await RunAsync(db, SyntheticXlsx.Build(SioFixtures.Headers, rows));

        foreach (var school in await db.Schools.AsNoTracking().ToListAsync())
        {
            Assert.DoesNotContain(SioFixtures.OrganNameMarker, school.Name + school.Slug + school.Provenance.Method);
        }
    }

    [Fact]
    public async Task Missing_mapped_column_aborts_and_writes_nothing()
    {
        await using var db = TestDb.Create();
        var headersWithoutName = SioFixtures.Headers.Where(h => h != SioFixtures.Nazwa).ToArray();
        var rows = new[] { SioFixtures.School("1001", "Szkola Podstawowa nr 1 w Testowie", "130") };

        var exception = await Record.ExceptionAsync(() =>
            RunAsync(db, SyntheticXlsx.Build(headersWithoutName, rows)));

        Assert.NotNull(exception);
        Assert.Contains(SioFixtures.Nazwa, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, (await TestDb.CountAsync(db)).Schools);
    }

    [Fact]
    public async Task Personal_data_column_aborts_the_whole_file()
    {
        await using var db = TestDb.Create();
        var headers = SioFixtures.Headers.Append("imie").ToArray();
        var row = SioFixtures.School("1001", "Szkola Podstawowa nr 1 w Testowie", "141");
        row["imie"] = "TEST-VALUE";

        var exception = await Record.ExceptionAsync(() =>
            RunAsync(db, SyntheticXlsx.Build(headers, [row])));

        Assert.NotNull(exception);
        Assert.Contains("imie", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, (await TestDb.CountAsync(db)).Schools);
    }

    [Fact]
    public async Task Faith_affiliation_is_derived_from_governing_body_type_140_only()
    {
        await using var db = TestDb.Create();
        var rows = new[]
        {
            SioFixtures.School("2001", "Szkola Podstawowa imienia Patrona Testowego w Testowie", "130"),
            SioFixtures.School("2002", "Szkola Podstawowa nr 2 w Testowie", "140"),
        };

        await RunAsync(db, SyntheticXlsx.Build(SioFixtures.Headers, rows));

        var namedAfterPatron = await db.Schools.SingleAsync(s => s.IdPodmiot == 2001);
        var ecclesiastical = await db.Schools.SingleAsync(s => s.IdPodmiot == 2002);

        Assert.False(namedAfterPatron.FaithAffiliationDerived);
        Assert.True(ecclesiastical.FaithAffiliationDerived);
    }

    private static void AssertProvenance(Provenance provenance, DateOnly asOf)
    {
        Assert.False(string.IsNullOrWhiteSpace(provenance.Source));
        Assert.False(string.IsNullOrWhiteSpace(provenance.SourceUrl));
        Assert.False(string.IsNullOrWhiteSpace(provenance.Method));
        Assert.Equal(asOf, provenance.AsOf);
    }
}
