using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Import.Tests;

/// <summary>A fresh InMemory catalog per test. No migrations on InMemory; loaders see an empty schema.</summary>
internal static class TestDb
{
    public static CatalogDbContext Create()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new CatalogDbContext(options);
    }

    public static async Task<RowCounts> CountAsync(CatalogDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        return new RowCounts(
            await db.Schools.CountAsync(),
            await db.ClassCohorts.CountAsync(),
            await db.ExamResults.CountAsync(),
            await db.NationalBaselines.CountAsync(),
            await db.GoverningBodyTypes.CountAsync());
    }
}

internal sealed record RowCounts(
    int Schools,
    int ClassCohorts,
    int ExamResults,
    int NationalBaselines,
    int GoverningBodyTypes);

/// <summary>
/// Builds a synthetic SIO-shaped workbook in memory. Every cell is text. Rows are keyed by header name;
/// a missing key leaves the cell empty. Names and values are invented: school names only, no people.
/// </summary>
internal static class SyntheticXlsx
{
    public static MemoryStream Build(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyDictionary<string, string>> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Szkoly");

        for (var column = 0; column < headers.Count; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        var rowNumber = 2;
        foreach (var row in rows)
        {
            for (var column = 0; column < headers.Count; column++)
            {
                if (row.TryGetValue(headers[column], out var value))
                {
                    sheet.Cell(rowNumber, column + 1).Value = value;
                }
            }

            rowNumber++;
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}

/// <summary>Synthetic CSV text as a stream (UTF-8, no BOM).</summary>
internal static class CsvStream
{
    public static MemoryStream Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }
}
