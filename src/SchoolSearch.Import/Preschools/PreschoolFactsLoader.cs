using System.Globalization;
using CsvHelper;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Preschools;

/// <summary>Where a facts file came from. Given on the command line and copied to every row's provenance.</summary>
/// <param name="SourceName">Publisher or document name (at most 200 characters).</param>
/// <param name="SourceUrl">Absolute http(s) URL of the source (at most 500 characters).</param>
/// <param name="AsOf">The date the file describes.</param>
public sealed record PreschoolFactsSource(string SourceName, string SourceUrl, DateOnly AsOf);

/// <summary>
/// Loads practical facts (tuition, meals, opening hours, free places) for preschools that already exist, from a CSV file
/// into <see cref="PreschoolFacts"/>. The file is the only source: nothing is scraped or inferred.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Exact idPodmiot match only. An id that matches no <see cref="Preschool"/> is rejected.</item>
/// <item>Each row is parsed completely before it is written. A rejected row writes nothing; the other rows still load.</item>
/// <item>Money accepts a decimal point or a single decimal comma, at most two decimals, 0 to 99 999 999.99 PLN.
/// Thousands separators are refused rather than guessed.</item>
/// <item>Times are <c>HH:mm</c>; closing must be after opening when both are given.</item>
/// <item>A free-places count needs <c>freePlacesAsOf</c>, and a date without a count is refused.</item>
/// <item><c>confidence</c> is <c>reported</c> (default, primary public source) or <c>unverified</c>.</item>
/// <item>A row with no fact value at all is rejected.</item>
/// <item>Idempotent. Each facts row is replaced by key. Preschools absent from the file are not touched.</item>
/// <item>Only the optional columns that the file has are read; a missing optional column is null for every row.</item>
/// </list>
/// </remarks>
public static class PreschoolFactsLoader
{
    public const string Method = "csv import";
    public const string ConfidenceReported = "reported";
    public const string ConfidenceUnverified = "unverified";

    private const string AsOfFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";
    private const int MaxSourceNameLength = 200;
    private const int MaxSourceUrlLength = 500;
    private const decimal MaxMoneyPln = 99_999_999.99m;

    public static async Task<ImportReport> RunAsync(
        CatalogDbContext db,
        Stream csv,
        PreschoolFactsSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);

        var text = await CkeCsvSupport.ReadTextAsync(csv, ct);
        if (CkeCsvSupport.IsBlank(text))
        {
            throw new InvalidOperationException("Preschool facts import aborted: the file is empty. Nothing was written.");
        }

        using var textReader = new StringReader(text);
        using var reader = new CsvReader(textReader, CkeCsvSupport.CreateConfiguration(CkeCsvSupport.DetectDelimiter(text)));
        if (!reader.Read())
        {
            throw new InvalidOperationException("Preschool facts import aborted: the file has no header row. Nothing was written.");
        }

        reader.ReadHeader();
        var headers = reader.HeaderRecord ?? Array.Empty<string>();

        PersonalDataGuard.EnsureNoPersonalColumns(headers, "Preschool facts import");
        CkeCsvSupport.EnsureNoPersonNameColumns(headers, "Preschool facts import");

        var columns = ResolveColumns(headers);

        var preschoolIds = (await db.Preschools.AsNoTracking().Select(p => p.IdPodmiot).ToListAsync(ct)).ToHashSet();
        var existing = await db.PreschoolFacts.ToDictionaryAsync(f => f.PreschoolId, ct);

        var log = new RejectionLog();
        var seenIds = new HashSet<long>();
        var rowsRead = 0;
        var rowsWritten = 0;

        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowsRead++;
            var row = rowsRead + 1;

            var idText = Field(reader, columns.IdPodmiot);
            if (!CkeCsvSupport.TryParseExactId(idText, out var preschoolId))
            {
                log.Add($"row {row}: idPodmiot is not a plain integer; no fuzzy matching");
                continue;
            }

            if (!seenIds.Add(preschoolId))
            {
                log.Add($"row {row}: duplicate idPodmiot {preschoolId} in the file; the row is rejected");
                continue;
            }

            if (!preschoolIds.Contains(preschoolId))
            {
                log.Add($"row {row}: idPodmiot {preschoolId} matches no preschool; exact match only");
                continue;
            }

            var parsed = ParseRow(reader, columns, row, log);
            if (parsed is null)
            {
                continue;
            }

            if (existing.TryGetValue(preschoolId, out var facts))
            {
                ApplyTo(facts, parsed, source);
            }
            else
            {
                facts = new PreschoolFacts { PreschoolId = preschoolId };
                ApplyTo(facts, parsed, source);
                existing[preschoolId] = facts;
                db.PreschoolFacts.Add(facts);
            }

            rowsWritten++;
        }

        await db.SaveChangesAsync(ct);
        return new ImportReport(rowsRead, rowsWritten, log.Count, log.Reasons());
    }

    /// <summary>Returns the row's values, or null after adding the reason to <paramref name="log"/>.</summary>
    private static ParsedFacts? ParseRow(CsvReader reader, FactColumns columns, int row, RejectionLog log)
    {
        if (!TryParseOptionalMoney(Field(reader, columns.TuitionMin), out var tuitionMin))
        {
            log.Add($"row {row}: '{FactColumnNames.TuitionMin}' must be a PLN amount with at most two decimals");
            return null;
        }

        if (!TryParseOptionalMoney(Field(reader, columns.TuitionMax), out var tuitionMax))
        {
            log.Add($"row {row}: '{FactColumnNames.TuitionMax}' must be a PLN amount with at most two decimals");
            return null;
        }

        if (tuitionMin is decimal min && tuitionMax is decimal max && min > max)
        {
            log.Add($"row {row}: '{FactColumnNames.TuitionMin}' is above '{FactColumnNames.TuitionMax}'");
            return null;
        }

        if (!TryParseOptionalMoney(Field(reader, columns.Meal), out var meal))
        {
            log.Add($"row {row}: '{FactColumnNames.Meal}' must be a PLN amount with at most two decimals");
            return null;
        }

        if (!TryParseOptionalTime(Field(reader, columns.Opens), out var opens))
        {
            log.Add($"row {row}: '{FactColumnNames.Opens}' must be a time in HH:mm form");
            return null;
        }

        if (!TryParseOptionalTime(Field(reader, columns.Closes), out var closes))
        {
            log.Add($"row {row}: '{FactColumnNames.Closes}' must be a time in HH:mm form");
            return null;
        }

        if (opens is TimeOnly open && closes is TimeOnly close && close <= open)
        {
            log.Add($"row {row}: '{FactColumnNames.Closes}' is not after '{FactColumnNames.Opens}'");
            return null;
        }

        var freePlacesText = Field(reader, columns.FreePlaces);
        int? freePlaces = null;
        if (freePlacesText.Length > 0)
        {
            if (!CkeCsvSupport.TryParseCount(freePlacesText, out var count) || count < 0)
            {
                log.Add($"row {row}: '{FactColumnNames.FreePlaces}' must be a whole number, zero or more");
                return null;
            }

            freePlaces = count;
        }

        var freePlacesAsOfText = Field(reader, columns.FreePlacesAsOf);
        DateOnly? freePlacesAsOf = null;
        if (freePlacesAsOfText.Length > 0)
        {
            if (!DateOnly.TryParseExact(freePlacesAsOfText, AsOfFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
            {
                log.Add($"row {row}: '{FactColumnNames.FreePlacesAsOf}' must be a date in yyyy-MM-dd form");
                return null;
            }

            freePlacesAsOf = asOf;
        }

        if (freePlaces is not null && freePlacesAsOf is null)
        {
            log.Add($"row {row}: '{FactColumnNames.FreePlacesAsOf}' is required when '{FactColumnNames.FreePlaces}' is given");
            return null;
        }

        if (freePlaces is null && freePlacesAsOf is not null)
        {
            log.Add($"row {row}: '{FactColumnNames.FreePlaces}' is required when '{FactColumnNames.FreePlacesAsOf}' is given");
            return null;
        }

        var confidence = Field(reader, columns.Confidence);
        if (confidence.Length == 0)
        {
            confidence = ConfidenceReported;
        }

        if (confidence is not (ConfidenceReported or ConfidenceUnverified))
        {
            log.Add($"row {row}: '{FactColumnNames.Confidence}' must be '{ConfidenceReported}' or '{ConfidenceUnverified}'");
            return null;
        }

        if (tuitionMin is null && tuitionMax is null && meal is null && opens is null && closes is null && freePlaces is null)
        {
            log.Add($"row {row}: no fact values");
            return null;
        }

        return new ParsedFacts(tuitionMin, tuitionMax, meal, opens, closes, freePlaces, freePlacesAsOf, confidence);
    }

    /// <summary>Every row gets its own provenance instance: EF owned types must not be shared between owners.</summary>
    private static void ApplyTo(PreschoolFacts facts, ParsedFacts parsed, PreschoolFactsSource source)
    {
        facts.MonthlyTuitionMinPln = parsed.TuitionMin;
        facts.MonthlyTuitionMaxPln = parsed.TuitionMax;
        facts.MonthlyMealPln = parsed.Meal;
        facts.OpensAt = parsed.Opens;
        facts.ClosesAt = parsed.Closes;
        facts.FreePlaces = parsed.FreePlaces;
        facts.FreePlacesAsOf = parsed.FreePlacesAsOf;
        facts.Confidence = parsed.Confidence;
        facts.Provenance = CkeCsvSupport.NewProvenance(source.SourceName, source.SourceUrl, source.AsOf, Method);
    }

    /// <summary>Empty is null (returns true). A decimal point or one decimal comma; both separators, spaces or more than two decimals are refused.</summary>
    private static bool TryParseOptionalMoney(string text, out decimal? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (text.Contains('.') && text.Contains(','))
        {
            return false;
        }

        var normalized = text.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < 0 || parsed > MaxMoneyPln || decimal.Round(parsed, 2) != parsed)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Empty is null (returns true). Otherwise exactly <c>HH:mm</c>.</summary>
    private static bool TryParseOptionalTime(string text, out TimeOnly? value)
    {
        value = null;
        if (text.Length == 0)
        {
            return true;
        }

        if (!TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return false;
        }

        value = time;
        return true;
    }

    private static void ValidateSource(PreschoolFactsSource source)
    {
        if (string.IsNullOrWhiteSpace(source.SourceName) || source.SourceName.Trim().Length > MaxSourceNameLength)
        {
            throw new ArgumentException($"--source-name is required and at most {MaxSourceNameLength} characters.");
        }

        if (!CkeCsvSupport.IsAbsoluteHttpUrl(source.SourceUrl) || source.SourceUrl.Length > MaxSourceUrlLength)
        {
            throw new ArgumentException($"--source-url must be an absolute http(s) URL of at most {MaxSourceUrlLength} characters.");
        }
    }

    private static FactColumns ResolveColumns(IReadOnlyList<string> headers)
    {
        var resolved = CkeCsvSupport.ResolveColumns(headers, [FactColumnNames.IdPodmiot], "Preschool facts import");

        int? Optional(string name)
        {
            var index = CkeCsvSupport.FindColumn(headers, name);
            return index < 0 ? null : index;
        }

        return new FactColumns(
            IdPodmiot: resolved[FactColumnNames.IdPodmiot],
            TuitionMin: Optional(FactColumnNames.TuitionMin),
            TuitionMax: Optional(FactColumnNames.TuitionMax),
            Meal: Optional(FactColumnNames.Meal),
            Opens: Optional(FactColumnNames.Opens),
            Closes: Optional(FactColumnNames.Closes),
            FreePlaces: Optional(FactColumnNames.FreePlaces),
            FreePlacesAsOf: Optional(FactColumnNames.FreePlacesAsOf),
            Confidence: Optional(FactColumnNames.Confidence));
    }

    /// <summary>The raw trimmed cell, or empty when the column is absent from the file.</summary>
    private static string Field(CsvReader reader, int? index) =>
        index is int column ? CkeCsvSupport.Field(reader, column).Trim() : string.Empty;

    private static class FactColumnNames
    {
        public const string IdPodmiot = "idPodmiot";
        public const string TuitionMin = "tuitionMin";
        public const string TuitionMax = "tuitionMax";
        public const string Meal = "meal";
        public const string Opens = "opens";
        public const string Closes = "closes";
        public const string FreePlaces = "freePlaces";
        public const string FreePlacesAsOf = "freePlacesAsOf";
        public const string Confidence = "confidence";
    }

    private sealed record FactColumns(
        int IdPodmiot,
        int? TuitionMin,
        int? TuitionMax,
        int? Meal,
        int? Opens,
        int? Closes,
        int? FreePlaces,
        int? FreePlacesAsOf,
        int? Confidence);

    private sealed record ParsedFacts(
        decimal? TuitionMin,
        decimal? TuitionMax,
        decimal? Meal,
        TimeOnly? Opens,
        TimeOnly? Closes,
        int? FreePlaces,
        DateOnly? FreePlacesAsOf,
        string Confidence);
}
