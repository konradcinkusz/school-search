using System.Globalization;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Catchment;

/// <summary>
/// Imports catchment (obwód) street lists from a CSV file into <see cref="CatchmentStreet"/> and
/// <see cref="CatchmentCoverage"/>. Source of the data is a gmina's published resolution; this loader never
/// invents a street or a boundary.
/// </summary>
/// <remarks>
/// <para>Columns (header-driven, names matched case-insensitively, all required):
/// <c>gminaTeryt, schoolIdPodmiot, street, numberFrom, numberTo, parity, state, resolutionUrl, resolutionDate,
/// source, sourceUrl, asOf, method</c>. Full format: docs/runbooks/IMPORT.md, section "Catchment".</para>
/// <para>States: <c>street_list</c> rows carry a school, a street and optional numbers and parity.
/// <c>none</c> rows record that a gmina publishes no street list; they leave the school and street columns empty.
/// <c>polygon</c> is not loaded by this command.</para>
/// <para>Atomicity per gmina: if any row of a gmina is rejected, nothing is written for that gmina and its
/// previously loaded streets and coverage stay in place. A partial street list would make a missing street
/// look like "no school", so the whole gmina waits for a clean file. Gminas without rejections are replaced
/// in full (existing streets removed, new ones added) inside one SaveChanges, so re-running gives the same rows.</para>
/// <para>Unknown school ids are rejected (exact match only). Personal-data headers abort the file.</para>
/// </remarks>
public static class CatchmentLoader
{
    public const string StateNone = "none";
    public const string StateStreetList = "street_list";

    private const string AsOfFormat = "yyyy-MM-dd";
    private const int MaxNumber = 999999;
    private const int MaxUrlLength = 500;

    private static readonly string[] RequiredColumns =
    [
        "gminaTeryt",
        "schoolIdPodmiot",
        "street",
        "numberFrom",
        "numberTo",
        "parity",
        "state",
        "resolutionUrl",
        "resolutionDate",
        "source",
        "sourceUrl",
        "asOf",
        "method",
    ];

    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream csv,
        TimeProvider clock,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(clock);

        var text = await CkeCsvSupport.ReadTextAsync(csv, ct);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("Catchment import aborted: the file is empty. Nothing was written.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        if (!reader.Read())
        {
            throw new InvalidOperationException("Catchment import aborted: the file has no header row. Nothing was written.");
        }

        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();

        PersonalDataGuard.EnsureNoPersonalColumns(headers, "Catchment import");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "Catchment import");

        var columns = CkeCsvSupport.ResolveColumns(headers, RequiredColumns, "Catchment import");

        var schoolIds = (await db.Schools.AsNoTracking().Select(s => s.IdPodmiot).ToListAsync(ct)).ToHashSet();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var reasons = new List<string>();
        var validRows = new List<CatchmentRow>();
        var coverageByGmina = new Dictionary<string, CoverageFields>(StringComparer.Ordinal);
        var blockedGminas = new HashSet<string>(StringComparer.Ordinal);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var rowsRead = 0;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowsRead++;

            var outcome = ParseRow(reader, columns, schoolIds, today, rowsRead);
            if (outcome.Row is null)
            {
                reasons.Add(outcome.Reason!);
                if (outcome.Gmina is not null)
                {
                    blockedGminas.Add(outcome.Gmina);
                }

                continue;
            }

            var row = outcome.Row;
            var key = row.Key;
            if (!seenKeys.Add(key))
            {
                reasons.Add($"row {rowsRead}: exact duplicate of an earlier row; ignored");
                continue;
            }

            if (coverageByGmina.TryGetValue(row.Gmina, out var firstCoverage))
            {
                if (firstCoverage != row.Coverage)
                {
                    reasons.Add(
                        $"row {rowsRead}: state, resolution, source, sourceUrl, asOf or method differs from the first row of gmina {row.Gmina}");
                    blockedGminas.Add(row.Gmina);
                    continue;
                }
            }
            else
            {
                coverageByGmina[row.Gmina] = row.Coverage;
            }

            validRows.Add(row);
        }

        var targets = coverageByGmina.Keys
            .Where(gmina => !blockedGminas.Contains(gmina))
            .OrderBy(gmina => gmina, StringComparer.Ordinal)
            .ToList();

        foreach (var gmina in blockedGminas.OrderBy(g => g, StringComparer.Ordinal))
        {
            reasons.Add(
                $"gmina {gmina}: nothing written for this gmina because at least one of its rows was rejected; " +
                "previously loaded data for it is kept");
        }

        var written = validRows.Where(row => targets.Contains(row.Gmina, StringComparer.Ordinal)).ToList();

        await ReplaceAsync(db, targets, coverageByGmina, written, ct);

        return new ImportReport(
            rowsRead,
            written.Count,
            rowsRead - written.Count,
            CapReasons(reasons));
    }

    /// <summary>Replaces the streets and coverage of each target gmina. One SaveChanges for the whole run.</summary>
    private static async Task ReplaceAsync(
        CatalogDbContext db,
        List<string> targets,
        IReadOnlyDictionary<string, CoverageFields> coverageByGmina,
        List<CatchmentRow> written,
        CancellationToken ct)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var existingStreets = await db.CatchmentStreets
            .Where(c => targets.Contains(c.GminaTeryt))
            .ToListAsync(ct);
        db.CatchmentStreets.RemoveRange(existingStreets);

        var existingCoverage = (await db.CatchmentCoverages
                .Where(c => targets.Contains(c.GminaTeryt))
                .ToListAsync(ct))
            .ToDictionary(c => c.GminaTeryt, StringComparer.Ordinal);

        foreach (var gmina in targets)
        {
            var fields = coverageByGmina[gmina];

            if (!existingCoverage.TryGetValue(gmina, out var coverage))
            {
                coverage = new CatchmentCoverage { GminaTeryt = gmina };
                db.CatchmentCoverages.Add(coverage);
            }

            coverage.State = fields.State;
            coverage.ResolutionUrl = fields.ResolutionUrl;
            coverage.ResolutionDate = fields.ResolutionDate;
            coverage.Provenance = CkeCsvSupport.NewProvenance(fields.Source, fields.SourceUrl, fields.AsOf, fields.Method);
        }

        foreach (var row in written)
        {
            if (row.Street is null)
            {
                continue;
            }

            db.CatchmentStreets.Add(new CatchmentStreet
            {
                GminaTeryt = row.Gmina,
                SchoolId = row.Street.SchoolId,
                StreetNormalized = row.Street.Street,
                NumberFrom = row.Street.NumberFrom,
                NumberTo = row.Street.NumberTo,
                Parity = row.Street.Parity,
                Provenance = CkeCsvSupport.NewProvenance(row.Coverage.Source, row.Coverage.SourceUrl, row.Coverage.AsOf, row.Coverage.Method),
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static Parsed ParseRow(
        CsvReader reader,
        IReadOnlyDictionary<string, int> columns,
        IReadOnlySet<long> schoolIds,
        DateOnly today,
        int line)
    {
        string Get(string name) => CkeCsvSupport.Field(reader, columns[name]).Trim();

        var gminaText = Get("gminaTeryt");
        if (!IsTeryt(gminaText))
        {
            return Reject(null, line, "gminaTeryt must be exactly 7 digits");
        }

        var gmina = gminaText;

        var state = Get("state").ToLowerInvariant();
        if (state is not (StateNone or StateStreetList))
        {
            return Reject(gmina, line, "state must be 'none' or 'street_list' (polygon coverage is not loaded by this command)");
        }

        var source = Get("source");
        var sourceUrl = Get("sourceUrl");
        var method = Get("method");

        if (CkeCsvSupport.IsBlank(source) || CkeCsvSupport.IsBlank(sourceUrl) || CkeCsvSupport.IsBlank(method))
        {
            return Reject(gmina, line, "source, sourceUrl and method must all be filled");
        }

        if (!CkeCsvSupport.IsAbsoluteHttpUrl(sourceUrl))
        {
            return Reject(gmina, line, "sourceUrl is not an absolute http(s) URL");
        }

        if (!CkeCsvSupport.FitsProvenance(source, sourceUrl, method))
        {
            return Reject(gmina, line, "provenance text is too long for storage");
        }

        if (!DateOnly.TryParseExact(Get("asOf"), AsOfFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
        {
            return Reject(gmina, line, "asOf must be a date in yyyy-MM-dd form");
        }

        if (asOf > today)
        {
            return Reject(gmina, line, "asOf is later than today; the file date cannot be in the future");
        }

        var resolutionUrl = Get("resolutionUrl");
        string? resolutionUrlValue = CkeCsvSupport.IsBlank(resolutionUrl) ? null : resolutionUrl;
        if (resolutionUrlValue is not null
            && (resolutionUrlValue.Length > MaxUrlLength || !CkeCsvSupport.IsAbsoluteHttpUrl(resolutionUrlValue)))
        {
            return Reject(gmina, line, "resolutionUrl must be an absolute http(s) URL of at most 500 characters");
        }

        if (state == StateStreetList && resolutionUrlValue is null)
        {
            return Reject(gmina, line, "resolutionUrl is required when state is street_list (the public page links it)");
        }

        DateOnly? resolutionDate = null;
        var resolutionDateText = Get("resolutionDate");
        if (!CkeCsvSupport.IsBlank(resolutionDateText))
        {
            if (!DateOnly.TryParseExact(resolutionDateText, AsOfFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                return Reject(gmina, line, "resolutionDate must be a date in yyyy-MM-dd form or empty");
            }

            resolutionDate = parsedDate;
        }

        var coverage = new CoverageFields(state, resolutionUrlValue, resolutionDate, source, sourceUrl, asOf, method);

        var schoolText = Get("schoolIdPodmiot");
        var street = Get("street");
        var numberFromText = Get("numberFrom");
        var numberToText = Get("numberTo");
        var parity = Get("parity").ToLowerInvariant();

        if (state == StateNone)
        {
            if (!CkeCsvSupport.IsBlank(schoolText) || !CkeCsvSupport.IsBlank(street)
                || !CkeCsvSupport.IsBlank(numberFromText) || !CkeCsvSupport.IsBlank(numberToText)
                || !CkeCsvSupport.IsBlank(parity))
            {
                return Reject(gmina, line, "state 'none' rows must leave schoolIdPodmiot, street, numberFrom, numberTo and parity empty");
            }

            return new Parsed(gmina, new CatchmentRow(gmina, coverage, null, $"{gmina}|{state}"), null);
        }

        if (!CkeCsvSupport.TryParseExactId(schoolText, out var schoolId))
        {
            return Reject(gmina, line, "schoolIdPodmiot must be a plain integer idPodmiot; no fuzzy matching");
        }

        if (!schoolIds.Contains(schoolId))
        {
            return Reject(gmina, line, $"schoolIdPodmiot {schoolId} matches no School; exact match only");
        }

        var normalizedStreet = StreetNormalizer.Normalize(street);
        if (normalizedStreet.Length == 0 || normalizedStreet.Length > StreetNormalizer.MaxLength)
        {
            return Reject(gmina, line, "street must contain a street name of at most 200 characters after normalisation");
        }

        int? numberFrom = null;
        if (!CkeCsvSupport.IsBlank(numberFromText))
        {
            if (!CkeCsvSupport.TryParseCount(numberFromText, out var fromValue) || fromValue < 1 || fromValue > MaxNumber)
            {
                return Reject(gmina, line, "numberFrom must be a whole number from 1 to 999999, or empty");
            }

            numberFrom = fromValue;
        }

        int? numberTo = null;
        if (!CkeCsvSupport.IsBlank(numberToText))
        {
            if (!CkeCsvSupport.TryParseCount(numberToText, out var toValue) || toValue < 1 || toValue > MaxNumber)
            {
                return Reject(gmina, line, "numberTo must be a whole number from 1 to 999999, or empty");
            }

            numberTo = toValue;
        }

        if (numberFrom is int low && numberTo is int high && low > high)
        {
            return Reject(gmina, line, "numberFrom is greater than numberTo");
        }

        string? parityValue = null;
        if (parity.Length > 0)
        {
            if (parity is not ("even" or "odd"))
            {
                return Reject(gmina, line, "parity must be 'even', 'odd' or empty");
            }

            parityValue = parity;
        }

        var streetRow = new StreetRow(schoolId, normalizedStreet, numberFrom, numberTo, parityValue);
        var key = $"{gmina}|{state}|{schoolId}|{normalizedStreet}|{numberFrom}|{numberTo}|{parityValue}";
        return new Parsed(gmina, new CatchmentRow(gmina, coverage, streetRow, key), null);
    }

    private static Parsed Reject(string? gmina, int line, string reason) =>
        new(gmina, null, $"row {line}: {reason}");

    private static bool IsTeryt(string value) => value.Length == 7 && value.All(char.IsAsciiDigit);

    private static IReadOnlyList<string> CapReasons(List<string> reasons)
    {
        var limit = CkeCsvSupport.MaxStoredRejectionReasons;
        if (reasons.Count <= limit)
        {
            return reasons;
        }

        var kept = reasons.Take(limit).ToList();
        kept.Add($"... {reasons.Count - limit} more rejection reasons not listed");
        return kept;
    }

    /// <summary>Coverage fields of one gmina. Every row of a gmina must carry the same values.</summary>
    private sealed record CoverageFields(
        string State,
        string? ResolutionUrl,
        DateOnly? ResolutionDate,
        string Source,
        string SourceUrl,
        DateOnly AsOf,
        string Method);

    private sealed record StreetRow(long SchoolId, string Street, int? NumberFrom, int? NumberTo, string? Parity);

    /// <summary>One accepted row. Key identifies exact duplicates within the file.</summary>
    private sealed record CatchmentRow(string Gmina, CoverageFields Coverage, StreetRow? Street, string Key);

    private sealed record Parsed(string? Gmina, CatchmentRow? Row, string? Reason);
}
