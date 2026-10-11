using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Import.Cke;

/// <summary>
/// Imports the national baseline (one national mean and optional pupil standard deviation per subject and year)
/// into <see cref="NationalBaselineRow"/>. The scoring engine reads these values; they are never invented here.
/// </summary>
/// <remarks>
/// Required columns: <c>subject</c> (polish | mathematics | foreign_language), <c>nationalMeanPercent</c> (0..100).
/// Optional columns: <c>pupilStdDevPoints</c> (empty gives null; a value must be greater than 0), and
/// <c>source</c>, <c>sourceUrl</c>, <c>method</c>. When the provenance columns are absent the stored values are
/// the explicit placeholders <see cref="UnverifiedSource"/> and <see cref="UnverifiedSourceUrl"/>, which must be
/// replaced by a verified source before the baseline is published.
/// A bad row is rejected and counted; the rest of the file is still written. Re-running is idempotent.
/// </remarks>
public static class BaselineLoader
{
    public const string UnverifiedSource = "UNVERIFIED: national baseline CSV without source columns";
    public const string UnverifiedSourceUrl = "UNVERIFIED";
    public const string Method = "csv import";

    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream csv,
        int examYear,
        TimeProvider clock,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(clock);

        var text = await CkeCsvSupport.ReadTextAsync(csv, ct);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("Baseline import aborted: the file is empty. Nothing was written.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();

        PersonalDataGuard.EnsureNoPersonalColumns(headers, "Baseline import");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "Baseline import");

        var columns = CkeCsvSupport.ResolveColumns(headers, new[] { "subject", "nationalMeanPercent" }, "Baseline import");
        var subjectColumn = columns["subject"];
        var meanColumn = columns["nationalMeanPercent"];
        var stdDevColumn = CkeCsvSupport.FindColumn(headers, "pupilStdDevPoints");
        var sourceColumn = CkeCsvSupport.FindColumn(headers, "source");
        var sourceUrlColumn = CkeCsvSupport.FindColumn(headers, "sourceUrl");
        var methodColumn = CkeCsvSupport.FindColumn(headers, "method");

        var existing = (await db.NationalBaselines.Where(b => b.ExamYear == examYear).ToListAsync(ct))
            .ToDictionary(b => b.Subject, StringComparer.Ordinal);

        var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var log = new RejectionLog();
        var seenSubjects = new HashSet<string>(StringComparer.Ordinal);
        var rowsRead = 0;
        var rowsWritten = 0;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowsRead++;
            var row = rowsRead;

            var subject = CkeCsvSupport.Field(reader, subjectColumn).Trim();
            if (!ExamSubjects.All.Contains(subject, StringComparer.Ordinal))
            {
                log.Add($"row {row}: unknown subject code; expected polish, mathematics or foreign_language");
                continue;
            }

            if (!seenSubjects.Add(subject))
            {
                log.Add($"row {row}: subject '{subject}' appears more than once; the row is rejected");
                continue;
            }

            var meanText = CkeCsvSupport.Field(reader, meanColumn);
            if (!CkeCsvSupport.TryParseDecimal(meanText, out var nationalMean) || nationalMean < 0 || nationalMean > 100)
            {
                log.Add($"row {row}: subject '{subject}': nationalMeanPercent must be a number from 0 to 100");
                continue;
            }

            double? stdDev = null;
            var stdDevText = CkeCsvSupport.Field(reader, stdDevColumn);
            if (!CkeCsvSupport.IsBlank(stdDevText))
            {
                if (!CkeCsvSupport.TryParseDecimal(stdDevText, out var parsedStdDev) || parsedStdDev <= 0)
                {
                    log.Add($"row {row}: subject '{subject}': pupilStdDevPoints must be a number greater than 0 when present");
                    continue;
                }

                stdDev = parsedStdDev;
            }

            var source = CkeCsvSupport.Field(reader, sourceColumn).Trim();
            var sourceUrl = CkeCsvSupport.Field(reader, sourceUrlColumn).Trim();
            var method = CkeCsvSupport.Field(reader, methodColumn).Trim();
            var hasSourceColumns = sourceColumn >= 0 || sourceUrlColumn >= 0 || methodColumn >= 0;

            if (!hasSourceColumns)
            {
                source = UnverifiedSource;
                sourceUrl = UnverifiedSourceUrl;
                method = Method;
            }
            else
            {
                if (CkeCsvSupport.IsBlank(source) || CkeCsvSupport.IsBlank(sourceUrl) || CkeCsvSupport.IsBlank(method))
                {
                    log.Add($"row {row}: subject '{subject}': source, sourceUrl and method must all be filled");
                    continue;
                }

                if (!CkeCsvSupport.IsAbsoluteHttpUrl(sourceUrl))
                {
                    log.Add($"row {row}: subject '{subject}': sourceUrl is not an absolute http(s) URL");
                    continue;
                }
            }

            if (!CkeCsvSupport.FitsProvenance(source, sourceUrl, method))
            {
                log.Add($"row {row}: subject '{subject}': provenance text is empty or too long");
                continue;
            }

            if (existing.TryGetValue(subject, out var baseline))
            {
                baseline.NationalMeanPercent = nationalMean;
                baseline.PupilStdDevPoints = stdDev;
                baseline.Provenance = CkeCsvSupport.NewProvenance(source, sourceUrl, asOf, method);
            }
            else
            {
                var created = new NationalBaselineRow
                {
                    ExamYear = examYear,
                    Subject = subject,
                    NationalMeanPercent = nationalMean,
                    PupilStdDevPoints = stdDev,
                    Provenance = CkeCsvSupport.NewProvenance(source, sourceUrl, asOf, method),
                };

                db.NationalBaselines.Add(created);
                existing[subject] = created;
            }

            rowsWritten++;
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(rowsRead, rowsWritten, log.Count, log.Reasons());
    }
}
