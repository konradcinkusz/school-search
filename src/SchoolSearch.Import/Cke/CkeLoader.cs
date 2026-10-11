using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Import.Cke;

/// <summary>
/// Imports the CKE per-school exam export into <see cref="ExamResult"/> rows, one per school, year and subject.
/// </summary>
/// <remarks>
/// Joins only by exact match of the key column against School.IdPodmiot. An unmatched, malformed or duplicate
/// key rejects that row and is counted with a reason. A row is written only when every subject on it is valid,
/// so a bad row never leaves a partial write. Re-running is idempotent: existing rows for the same
/// (school, year, subject) are updated in place. Rows with fewer than 5 pupils who sat are stored as given;
/// suppression is the scoring engine's job, not the loader's.
/// Pupil names are never read: only the mapped columns are accessed, and a name-like header aborts the run.
/// </remarks>
public static class CkeLoader
{
    public const string SourceName = "CKE/OKE Mapa Egzaminow";
    public const string SourceUrl = "https://mapa.wyniki.edu.pl/MapaEgzaminow/";
    public const string Method = "csv import";

    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream csv,
        int examYear,
        CkeColumnMap map,
        TimeProvider clock,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(clock);
        map.Validate();

        var text = await CkeCsvSupport.ReadTextAsync(csv, ct);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("CKE import aborted: the file is empty. Nothing was written.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(map.Delimiter));
        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();

        PersonalDataGuard.EnsureNoPersonalColumns(headers, "CKE import");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "CKE import");

        var subjects = map.SubjectColumns();
        var required = new List<string> { map.Key };
        foreach (var subject in subjects)
        {
            required.Add(subject.MeanPercentColumn);
            required.Add(subject.PupilsSatColumn);
        }

        var columns = CkeCsvSupport.ResolveColumns(headers, required, "CKE import");

        var schoolIds = (await db.Schools.AsNoTracking().Select(s => s.IdPodmiot).ToListAsync(ct)).ToHashSet();

        var existing = (await db.ExamResults.Where(r => r.ExamYear == examYear).ToListAsync(ct))
            .ToDictionary(r => (r.SchoolId, r.Subject));

        var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var log = new RejectionLog();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var parsed = new List<ParsedSubject>();
        var rowsRead = 0;
        var rowsWritten = 0;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowsRead++;
            var row = rowsRead;

            var keyText = CkeCsvSupport.Field(reader, columns[map.Key]);
            if (!CkeCsvSupport.TryParseExactId(keyText, out var schoolId))
            {
                log.Add($"row {row}: key column '{map.Key}' is not a plain integer idPodmiot; no fuzzy matching");
                continue;
            }

            if (!seenKeys.Add(keyText))
            {
                log.Add($"row {row}: duplicate key in file; the row is rejected");
                continue;
            }

            if (!schoolIds.Contains(schoolId))
            {
                log.Add($"row {row}: key matches no School.IdPodmiot; exact match only");
                continue;
            }

            parsed.Clear();
            var error = ReadSubjects(reader, subjects, columns, parsed);
            if (error is not null)
            {
                log.Add($"row {row}: {error}");
                continue;
            }

            if (parsed.Count == 0)
            {
                log.Add($"row {row}: no subject values");
                continue;
            }

            foreach (var result in parsed)
            {
                Upsert(db, existing, schoolId, examYear, result, asOf);
            }

            rowsWritten++;
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(rowsRead, rowsWritten, log.Count, log.Reasons());
    }

    /// <summary>
    /// Reads and validates every subject of the current row. Returns null when the row is valid, otherwise the reason.
    /// Only the mapped columns are touched.
    /// </summary>
    private static string? ReadSubjects(
        CsvReader reader,
        IReadOnlyList<CkeSubjectColumns> subjects,
        IReadOnlyDictionary<string, int> columns,
        List<ParsedSubject> output)
    {
        foreach (var subject in subjects)
        {
            var meanText = CkeCsvSupport.Field(reader, columns[subject.MeanPercentColumn]);
            var satText = CkeCsvSupport.Field(reader, columns[subject.PupilsSatColumn]);

            if (CkeCsvSupport.IsBlank(meanText) && CkeCsvSupport.IsBlank(satText))
            {
                continue;
            }

            if (CkeCsvSupport.IsBlank(meanText) || CkeCsvSupport.IsBlank(satText))
            {
                return $"subject '{subject.Subject}': mean and pupils-sat columns must both be filled or both empty";
            }

            if (!CkeCsvSupport.TryParseDecimal(meanText, out var mean))
            {
                return $"subject '{subject.Subject}': '{subject.MeanPercentColumn}' is not a number";
            }

            if (mean < 0 || mean > 100)
            {
                return $"subject '{subject.Subject}': '{subject.MeanPercentColumn}' is outside 0..100";
            }

            if (!CkeCsvSupport.TryParseCount(satText, out var sat))
            {
                return $"subject '{subject.Subject}': '{subject.PupilsSatColumn}' is not a whole number";
            }

            if (sat < 0)
            {
                return $"subject '{subject.Subject}': '{subject.PupilsSatColumn}' is negative";
            }

            output.Add(new ParsedSubject(subject.Subject, mean, sat));
        }

        return null;
    }

    private static void Upsert(
        CatalogDbContext db,
        Dictionary<(long SchoolId, string Subject), ExamResult> existing,
        long schoolId,
        int examYear,
        ParsedSubject result,
        DateOnly asOf)
    {
        var key = (schoolId, result.Subject);
        if (existing.TryGetValue(key, out var row))
        {
            row.MeanPercent = result.MeanPercent;
            row.PupilsSat = result.PupilsSat;
            row.Provenance = CkeCsvSupport.NewProvenance(SourceName, SourceUrl, asOf, Method);
            return;
        }

        var created = new ExamResult
        {
            SchoolId = schoolId,
            ExamYear = examYear,
            Subject = result.Subject,
            MeanPercent = result.MeanPercent,
            PupilsSat = result.PupilsSat,
            Provenance = CkeCsvSupport.NewProvenance(SourceName, SourceUrl, asOf, Method),
        };

        db.ExamResults.Add(created);
        existing[key] = created;
    }

    private sealed record ParsedSubject(string Subject, double MeanPercent, int PupilsSat);
}
