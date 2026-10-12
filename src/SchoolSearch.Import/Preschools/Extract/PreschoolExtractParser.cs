using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Preschools.Extract;

/// <summary>One proposed fact that passed every check. Still keyed by the name as written; matching comes later.</summary>
public sealed record CandidateFact(
    int Chunk,
    string NameAsWritten,
    decimal? TuitionMin,
    decimal? TuitionMax,
    decimal? Meal,
    TimeOnly? Opens,
    TimeOnly? Closes,
    int? FreePlaces,
    DateOnly? FreePlacesAsOf,
    string Quote,
    IReadOnlyList<string> Notes);

/// <summary>
/// What one chunk's reply yielded. A failed chunk (refusal, cut-off, not one JSON object) contributes no facts at all.
/// </summary>
public sealed record ChunkParse(IReadOnlyList<CandidateFact> Facts, IReadOnlyList<string> Rejects, bool Failed);

/// <summary>
/// Strict reading of the model reply. A row is kept only when every value it states is valid AND the name, the quote
/// and every amount or count occur in the source text. Unknown keys are ignored. Nothing is repaired or guessed.
/// </summary>
public static class PreschoolExtractParser
{
    private const int MaxNameLength = 200;
    private const int MaxQuoteLength = 300;
    private const string TimeFormat = "HH:mm";
    private const string DateFormat = "yyyy-MM-dd";

    public static ChunkParse Parse(int chunk, string chunkText, string? reply, DateOnly asOf)
    {
        var facts = new List<CandidateFact>();
        var rejects = new List<string>();

        if (string.IsNullOrWhiteSpace(reply))
        {
            rejects.Add($"chunk {chunk}: the model gave no text (refusal or cut-off); no rows taken from it");
            return new ChunkParse(facts, rejects, Failed: true);
        }

        using var document = ModelJson.TryParseObject(reply);
        if (document is null
            || !document.RootElement.TryGetProperty("rows", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            rejects.Add($"chunk {chunk}: the reply is not one JSON object with a rows array; no rows taken from it");
            return new ChunkParse(facts, rejects, Failed: true);
        }

        var number = 0;
        foreach (var element in rows.EnumerateArray())
        {
            number++;
            var label = $"chunk {chunk} row {number}";
            if (element.ValueKind != JsonValueKind.Object)
            {
                rejects.Add($"{label}: not an object");
                continue;
            }

            var outcome = ParseRow(chunk, chunkText, element, asOf);
            if (outcome.Fact is null)
            {
                rejects.Add($"{label}: {outcome.Reason}");
            }
            else
            {
                facts.Add(outcome.Fact);
            }
        }

        return new ChunkParse(facts, rejects, Failed: false);
    }

    private sealed record RowOutcome(CandidateFact? Fact, string? Reason);

    private static RowOutcome Reject(string reason) => new(null, reason);

    private static RowOutcome ParseRow(int chunk, string chunkText, JsonElement row, DateOnly asOf)
    {
        if (!TryText(row, "nameAsWritten", out var name, out var error))
        {
            return Reject(error ?? "nameAsWritten is invalid");
        }

        if (name is null || name.Length > MaxNameLength)
        {
            return Reject("nameAsWritten is missing or longer than 200 characters");
        }

        if (!TryText(row, "quote", out var quote, out error))
        {
            return Reject(error ?? "quote is invalid");
        }

        if (quote is null || quote.Length > MaxQuoteLength)
        {
            return Reject("quote is missing or longer than 300 characters");
        }

        if (!Grounded(name, chunkText))
        {
            return Reject("nameAsWritten is not in the table text verbatim");
        }

        if (!Grounded(quote, chunkText))
        {
            return Reject("quote is not in the table text verbatim");
        }

        var amounts = PreschoolMoney.AmountsIn(quote);
        var quoteNormalized = UntrustedText.CollapseWhitespace(quote);
        var notes = new List<string>();

        if (!TryMoney(row, "tuitionMin", amounts, out var tuitionMin, out error)
            || !TryMoney(row, "tuitionMax", amounts, out var tuitionMax, out error)
            || !TryMoney(row, "meal", amounts, out var meal, out error))
        {
            return Reject(error ?? "a money value is invalid");
        }

        if (!TryTime(row, "opens", quoteNormalized, out var opens, out error)
            || !TryTime(row, "closes", quoteNormalized, out var closes, out error))
        {
            return Reject(error ?? "a time value is invalid");
        }

        if (!ModelJson.TryInt(row, "freePlaces", out var freePlaces))
        {
            return Reject("freePlaces must be a whole number or null");
        }

        if (freePlaces is int count && !amounts.Contains((decimal)count))
        {
            return Reject("freePlaces is not written in the quote");
        }

        if (!TryText(row, "freePlacesAsOf", out var asOfText, out error))
        {
            return Reject(error ?? "freePlacesAsOf is invalid");
        }

        DateOnly? freePlacesAsOf = null;
        if (asOfText is not null)
        {
            if (!DateOnly.TryParseExact(asOfText, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return Reject("freePlacesAsOf must be yyyy-MM-dd or null");
            }

            freePlacesAsOf = date;
        }

        // A count without a date on or before --as-of is not a fact anyone can date: the count is dropped, the rest of the row stays.
        if (freePlaces is not null && (freePlacesAsOf is null || freePlacesAsOf.Value > asOf))
        {
            notes.Add("freePlaces dropped: no freePlacesAsOf on or before the as-of date");
            freePlaces = null;
            freePlacesAsOf = null;
        }
        else if (freePlaces is null && freePlacesAsOf is not null)
        {
            notes.Add("freePlacesAsOf dropped: no freePlaces count");
            freePlacesAsOf = null;
        }

        if (tuitionMin is null && tuitionMax is null && meal is null && opens is null && closes is null && freePlaces is null)
        {
            return Reject("no fact values in the row");
        }

        if (tuitionMin is decimal low && tuitionMax is decimal high && low > high)
        {
            return Reject("tuitionMin is above tuitionMax");
        }

        if (opens is TimeOnly start && closes is TimeOnly end && end <= start)
        {
            return Reject("closes is not after opens");
        }

        return new RowOutcome(
            new CandidateFact(chunk, name, tuitionMin, tuitionMax, meal, opens, closes, freePlaces, freePlacesAsOf, quote, notes),
            null);
    }

    /// <summary>True when the text occurs verbatim in the chunk, either as written or as the escaped form the model was shown.</summary>
    private static bool Grounded(string text, string chunkText) =>
        UntrustedText.IsVerbatim(chunkText, text)
        || UntrustedText.IsVerbatim(UntrustedText.Escape(chunkText), text);

    /// <summary>A string or null. Any other JSON type is an error, never a silent null.</summary>
    private static bool TryText(JsonElement row, string key, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (!row.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"{key} must be a string or null";
            return false;
        }

        var text = element.GetString()?.Trim();
        value = string.IsNullOrEmpty(text) ? null : text;
        return true;
    }

    private static bool TryMoney(
        JsonElement row,
        string key,
        IReadOnlyList<decimal> amounts,
        out decimal? value,
        out string? error)
    {
        value = null;
        if (!TryText(row, key, out var text, out error))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        if (!PreschoolMoney.TryParseAmount(text, out var amount))
        {
            error = $"{key} must be a PLN amount with at most two decimals";
            return false;
        }

        if (!amounts.Contains(amount))
        {
            error = $"{key} amount is not written in the quote";
            return false;
        }

        value = amount;
        return true;
    }

    private static bool TryTime(
        JsonElement row,
        string key,
        string quoteNormalized,
        out TimeOnly? value,
        out string? error)
    {
        value = null;
        if (!TryText(row, key, out var text, out error))
        {
            return false;
        }

        if (text is null)
        {
            return true;
        }

        if (!TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            error = $"{key} must be HH:mm or null";
            return false;
        }

        if (!quoteNormalized.Contains(text, StringComparison.Ordinal))
        {
            error = $"{key} is not written in the quote";
            return false;
        }

        value = time;
        return true;
    }
}

/// <summary>Amounts in PLN. The same rules as <c>PreschoolFactsLoader</c>, so every value this tool writes loads.</summary>
public static partial class PreschoolMoney
{
    private const decimal MaxPln = 99_999_999.99m;

    /// <summary>
    /// A decimal point or one decimal comma, at most two decimals, 0 to 99 999 999.99. Thousands separators, a
    /// point and a comma together, signs and spaces are refused rather than guessed.
    /// </summary>
    public static bool TryParseAmount(string text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || (text.Contains('.') && text.Contains(',')))
        {
            return false;
        }

        // A space is accepted only as a thousands separator in groups of three ("1 200", "1 200,50"); any other space is refused.
        var compact = text;
        if (text.Contains(' '))
        {
            if (!ThousandsSpaced().IsMatch(text))
            {
                return false;
            }

            compact = text.Replace(" ", string.Empty, StringComparison.Ordinal);
        }

        if (!decimal.TryParse(compact.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < 0 || parsed > MaxPln || decimal.Round(parsed, 2) != parsed)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Every amount or count written in the text, read the same way the parser reads a value (1 200, 450,50, 520).</summary>
    public static IReadOnlyList<decimal> AmountsIn(string text)
    {
        var amounts = new List<decimal>();
        foreach (Match match in AmountToken().Matches(text))
        {
            var digits = new string(
                match.Value
                    .Where(c => char.IsDigit(c) || c is '.' or ',')
                    .Select(c => c == ',' ? '.' : c)
                    .ToArray());

            if (decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            {
                amounts.Add(value);
            }
        }

        return amounts;
    }

    [GeneratedRegex(@"(?<!\d)\d{1,3}(?:[  ]\d{3})+(?:[.,]\d{1,2})?(?!\d)|(?<!\d)\d+(?:[.,]\d{1,2})?(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex AmountToken();

    [GeneratedRegex(@"^[0-9]{1,3}(?: [0-9]{3})+(?:[.,][0-9]{1,2})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThousandsSpaced();
}
