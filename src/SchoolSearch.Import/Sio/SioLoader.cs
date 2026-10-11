using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Import;

/// <summary>
/// Loads the SIO school register (xlsx, dane.gov.pl dataset 288) into School, ClassCohort and GoverningBodyType.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Reads the first worksheet. The header row is found by searching the first rows for the idPodmiot header,
/// so column order does not matter.</item>
/// <item>Fail fast: if any mapped header is missing, or a mapped header appears twice, the run throws
/// <see cref="InvalidDataException"/> before anything is written.</item>
/// <item>Keeps only primary schools (TypPodmiotu equals the map value) that have at least one pupil.
/// Other rows are counted in RowsRead and neither written nor rejected.</item>
/// <item>Rows with unparsable mapped values are rejected with a reason and counted. Nothing is fuzzy-matched.</item>
/// <item>Idempotent. Schools are upserted by IdPodmiot, cohorts by (SchoolId, SchoolYear, ClassLevel). Running
/// twice gives the same counts and values. A stored slug is never changed. Coordinates and the declared faith
/// flag, which other loaders own, are preserved.</item>
/// <item>The organ-prowadzacy NAME column is never read. Only the type code (idTypOrgProw) is stored.</item>
/// </list>
/// RowsWritten counts School rows written. ClassCohort rows are not included in it.
/// </remarks>
public static class SioLoader
{
    public const string SourceName = "SIO / dane.gov.pl dataset 288";
    public const string SourceUrl = "https://dane.gov.pl/pl/dataset/288";
    public const string MethodName = "xlsx import";

    private const int BatchSize = 500;
    private const int HeaderSearchRows = 10;
    private const int GminaTerytLength = 7;
    private const char Bom = '﻿';

    /// <param name="asOf">
    /// The date the file describes (AsOf in provenance). Defaults to the UTC date of <paramref name="clock"/>.
    /// </param>
    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream xlsx,
        int schoolYear,
        SioColumnMap map,
        TimeProvider clock,
        CancellationToken ct,
        DateOnly? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(xlsx);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(clock);
        map.Validate();

        var provenanceDate = asOf ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Everything that can fail on the file runs before the first write.
        using var workbook = new XLWorkbook(xlsx);
        if (!workbook.Worksheets.Any())
        {
            throw new InvalidDataException("The SIO workbook has no worksheets.");
        }

        var sheet = workbook.Worksheet(1);
        var columns = ResolveColumns(sheet, map, out var headerRow, out var lastRow);

        var existingSchools = await db.Schools.AsNoTracking().ToDictionaryAsync(s => s.IdPodmiot, ct);
        var knownGoverningBodyCodes = (await db.GoverningBodyTypes.AsNoTracking().Select(g => g.Code).ToListAsync(ct)).ToHashSet();

        // Tracked on purpose: class-level rows are replaced by key within the year, including removal of stale levels.
        var cohortsByKey = (await db.ClassCohorts
                .Where(c => c.SchoolYear == schoolYear)
                .ToListAsync(ct))
            .ToDictionary(c => (c.SchoolId, c.ClassLevel));

        foreach (var entry in GoverningBodyDictionary.Entries)
        {
            if (knownGoverningBodyCodes.Add(entry.Code))
            {
                db.GoverningBodyTypes.Add(new GoverningBodyType
                {
                    Code = entry.Code,
                    Label = entry.Label,
                    LabelVerified = entry.LabelVerified,
                    IsNaturalPerson = entry.IsNaturalPerson,
                    Provenance = NewDictionaryProvenance(provenanceDate, "seed from product brief; code list unverified (docs/data/SOURCES.md 3.2)"),
                });
            }
        }

        var rejections = new List<string>();
        var rowsRead = 0;
        var rowsWritten = 0;
        var rowsInBatch = 0;
        var seenSchoolIds = new HashSet<long>();
        var completeSchoolIds = new HashSet<long>();
        var writtenCohortKeys = new HashSet<(long SchoolId, string ClassLevel)>();

        void Reject(int row, string reason) => rejections.Add($"row {row}: {reason}");

        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            ct.ThrowIfCancellationRequested();

            var idText = Text(sheet, row, columns.IdPodmiot);
            var typeText = Text(sheet, row, columns.TypPodmiotu);
            var nameText = Text(sheet, row, columns.Name);
            if (idText.Length == 0 && typeText.Length == 0 && nameText.Length == 0)
            {
                continue;
            }

            rowsRead++;

            if (!SameText(typeText, map.PrimarySchoolType))
            {
                continue;
            }

            if (!TryParseId(idText, out var idPodmiot))
            {
                Reject(row, $"'{map.IdPodmiot}' is not a positive integer");
                continue;
            }

            if (!seenSchoolIds.Add(idPodmiot))
            {
                Reject(row, $"duplicate idPodmiot {idPodmiot} in the file; first occurrence kept");
                continue;
            }

            if (nameText.Length == 0)
            {
                Reject(row, $"'{map.Name}' is empty");
                continue;
            }

            if (!TryParseTeryt(Text(sheet, row, columns.TerytGmina), out var gminaTeryt))
            {
                Reject(row, $"'{map.TerytGmina}' is not a 7-digit TERYT code");
                continue;
            }

            if (!TryParseOptionalCode(Text(sheet, row, columns.OrganProwTypeCode), out var governingBodyCode))
            {
                Reject(row, $"'{map.OrganProwTypeCode}' is not an integer");
                continue;
            }

            var levels = new List<(string Level, int Pupils, int Oddzialy)>(columns.Levels.Count);
            var levelError = (string?)null;
            foreach (var level in columns.Levels)
            {
                if (!TryParseCount(Text(sheet, row, level.PupilColumn), out var pupils))
                {
                    levelError = $"'{level.PupilHeader}' is not a whole number";
                    break;
                }

                if (!TryParseCount(Text(sheet, row, level.OddzialColumn), out var oddzialy))
                {
                    levelError = $"'{level.OddzialHeader}' is not a whole number";
                    break;
                }

                levels.Add((level.Level, pupils, oddzialy));
            }

            if (levelError is not null)
            {
                Reject(row, levelError);
                continue;
            }

            // The row parsed completely. Its existing cohorts for this year are authoritative from here on,
            // including the case where it now has no pupils and is therefore not served.
            completeSchoolIds.Add(idPodmiot);

            var totalPupils = levels.Sum(l => l.Pupils);
            if (totalPupils == 0)
            {
                continue;
            }

            var isPublic = SameText(Text(sheet, row, columns.Publicznosc), map.PublicValue);
            var specialText = Text(sheet, row, columns.SpecyfikaSzkoly).ToLowerInvariant();
            var isSpecial = specialText.Contains(map.SpecialSchoolMarker.ToLowerInvariant(), StringComparison.Ordinal);

            School school;
            var isNew = !existingSchools.TryGetValue(idPodmiot, out var existing);
            if (isNew)
            {
                school = new School
                {
                    IdPodmiot = idPodmiot,
                    Slug = SchoolSlug.Create(nameText, idPodmiot),
                };
            }
            else
            {
                // Keeps the stored slug, coordinates and declared faith flag: only file-owned fields are overwritten.
                school = existing!;
            }

            school.Name = nameText;
            school.GminaTeryt = gminaTeryt;
            school.GoverningBodyTypeCode = governingBodyCode;
            school.IsPublic = isPublic;
            school.IsSpecial = isSpecial;
            school.FaithAffiliationDerived = FaithDerivation.IsDerivedFromGoverningBody(governingBodyCode);
            school.Provenance = NewProvenance(provenanceDate);

            if (governingBodyCode is int code && knownGoverningBodyCodes.Add(code))
            {
                // Unknown code: a dictionary row is created unverified. IsNaturalPerson stays false until the
                // official list is checked (docs/data/SOURCES.md 3.2). Names are never stored, so this cannot expose one.
                db.GoverningBodyTypes.Add(new GoverningBodyType
                {
                    Code = code,
                    Label = null,
                    LabelVerified = false,
                    IsNaturalPerson = false,
                    Provenance = NewDictionaryProvenance(provenanceDate, "code seen in SIO file; not in seed list; unverified"),
                });
            }

            if (isNew)
            {
                db.Schools.Add(school);
            }
            else
            {
                db.Schools.Update(school);
            }

            foreach (var (level, pupils, oddzialy) in levels)
            {
                if (pupils == 0 && oddzialy == 0)
                {
                    continue;
                }

                var key = (idPodmiot, level);
                writtenCohortKeys.Add(key);

                if (cohortsByKey.TryGetValue(key, out var cohort))
                {
                    cohort.Pupils = pupils;
                    cohort.Oddzialy = oddzialy;
                    cohort.Provenance = NewProvenance(provenanceDate);
                }
                else
                {
                    cohort = new ClassCohort
                    {
                        SchoolId = idPodmiot,
                        SchoolYear = schoolYear,
                        ClassLevel = level,
                        Pupils = pupils,
                        Oddzialy = oddzialy,
                        Provenance = NewProvenance(provenanceDate),
                    };
                    db.ClassCohorts.Add(cohort);
                    cohortsByKey[key] = cohort;
                }
            }

            rowsWritten++;
            rowsInBatch++;
            if (rowsInBatch >= BatchSize)
            {
                await db.SaveChangesAsync(ct);
                rowsInBatch = 0;
            }
        }

        // Cohort levels that a complete row no longer reports are removed, so a re-import is an exact replica.
        foreach (var (key, cohort) in cohortsByKey)
        {
            if (completeSchoolIds.Contains(key.SchoolId) && !writtenCohortKeys.Contains(key))
            {
                db.ClassCohorts.Remove(cohort);
            }
        }

        await db.SaveChangesAsync(ct);

        return new ImportReport(rowsRead, rowsWritten, rejections.Count, rejections);
    }

    private static Provenance NewProvenance(DateOnly asOf) => new()
    {
        Source = SourceName,
        SourceUrl = SourceUrl,
        AsOf = asOf,
        Method = MethodName,
    };

    /// <summary>Dictionary rows are not taken from a verified list, so their provenance says so.</summary>
    private static Provenance NewDictionaryProvenance(DateOnly asOf, string method) => new()
    {
        Source = "SIO dictionary (idTypOrgProw), unverified",
        SourceUrl = SourceUrl,
        AsOf = asOf,
        Method = method,
    };

    private static SioColumns ResolveColumns(IXLWorksheet sheet, SioColumnMap map, out int headerRow, out int lastRow)
    {
        lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        if (lastRow == 0 || lastColumn == 0)
        {
            throw new InvalidDataException("The first worksheet of the SIO workbook is empty.");
        }

        var (columns, duplicates) = ReadHeaders(sheet, 1, lastColumn);
        headerRow = 1;
        for (var row = 1; row <= Math.Min(HeaderSearchRows, lastRow); row++)
        {
            var candidate = ReadHeaders(sheet, row, lastColumn);
            if (candidate.Columns.ContainsKey(map.IdPodmiot))
            {
                (columns, duplicates) = candidate;
                headerRow = row;
                break;
            }
        }

        // The whole file is refused when it carries a personal-data column, before anything is read.
        PersonalDataGuard.EnsureNoPersonalColumns(columns.Keys, "SIO import");

        var required = map.RequiredHeaders();

        var missing = required
            .Where(h => !columns.ContainsKey(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"The SIO file is missing {missing.Count} mapped column(s) in header row {headerRow}: " +
                $"{string.Join(", ", missing)}. No rows were written. The SioColumnMap defaults are UNVERIFIED; " +
                "set the map from the real header row.");
        }

        var ambiguous = required
            .Where(h => duplicates.Contains(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ambiguous.Count > 0)
        {
            throw new InvalidDataException(
                $"Mapped column(s) appear more than once in header row {headerRow}: {string.Join(", ", ambiguous)}. No rows were written.");
        }

        var levels = new List<LevelColumns>(map.PupilColumns.Count);
        foreach (var (level, pupilHeader) in map.PupilColumns)
        {
            var oddzialHeader = map.OddzialColumns[level];
            levels.Add(new LevelColumns(
                level,
                pupilHeader,
                columns[pupilHeader],
                oddzialHeader,
                columns[oddzialHeader]));
        }

        return new SioColumns(
            IdPodmiot: columns[map.IdPodmiot],
            TypPodmiotu: columns[map.TypPodmiotu],
            Name: columns[map.Name],
            TerytGmina: columns[map.TerytGmina],
            Publicznosc: columns[map.Publicznosc],
            SpecyfikaSzkoly: columns[map.SpecyfikaSzkoly],
            OrganProwTypeCode: columns[map.OrganProwTypeCode],
            Levels: levels);
    }

    private static (Dictionary<string, int> Columns, HashSet<string> Duplicates) ReadHeaders(IXLWorksheet sheet, int row, int lastColumn)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var column = 1; column <= lastColumn; column++)
        {
            var header = Text(sheet, row, column);
            if (header.Length == 0)
            {
                continue;
            }

            if (!columns.TryAdd(header, column))
            {
                duplicates.Add(header);
            }
        }

        return (columns, duplicates);
    }

    private static string Text(IXLWorksheet sheet, int row, int column) =>
        sheet.Cell(row, column).GetString().Trim().Trim(Bom).Normalize(System.Text.NormalizationForm.FormC);

    private static bool SameText(string value, string expected) =>
        string.Equals(value, expected.Trim().Normalize(System.Text.NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase);

    private static bool TryParseId(string text, out long value) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

    /// <summary>Blank is 0. Otherwise a non-negative whole number, spaces allowed.</summary>
    private static bool TryParseCount(string text, out int value)
    {
        value = 0;
        var cleaned = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (cleaned.Length == 0)
        {
            return true;
        }

        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        if (number < 0 || number > int.MaxValue || Math.Floor(number) != number)
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    private static bool TryParseOptionalCode(string text, out int? code)
    {
        code = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!TryParseCount(text, out var value))
        {
            return false;
        }

        code = value;
        return true;
    }

    /// <summary>Empty is null. A purely numeric value shorter than 7 is zero-padded. Otherwise it must be 7 digits.</summary>
    private static bool TryParseTeryt(string text, out string? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!text.All(char.IsAsciiDigit) || text.Length > GminaTerytLength)
        {
            return false;
        }

        value = text.PadLeft(GminaTerytLength, '0');
        return true;
    }

    private sealed record LevelColumns(string Level, string PupilHeader, int PupilColumn, string OddzialHeader, int OddzialColumn);

    private sealed record SioColumns(
        int IdPodmiot,
        int TypPodmiotu,
        int Name,
        int TerytGmina,
        int Publicznosc,
        int SpecyfikaSzkoly,
        int OrganProwTypeCode,
        IReadOnlyList<LevelColumns> Levels);
}
