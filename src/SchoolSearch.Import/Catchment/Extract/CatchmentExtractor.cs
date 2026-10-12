using System.Globalization;
using System.Text.Json;
using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Catchment.Extract;

/// <summary>One candidate street rule, validated. Not a catchment fact until a human signs it off.</summary>
/// <param name="Chunk">1-based chunk number in the saved document.</param>
/// <param name="SchoolAsWritten">School name as it appears in the text.</param>
/// <param name="SchoolIdPodmiot">Exact match in the school list; null when no school list was given (fill in before load).</param>
/// <param name="Street">Street name as written (the loader normalises it).</param>
/// <param name="NumberFrom">Lowest house number of the rule, or null for no lower bound.</param>
/// <param name="NumberTo">Highest house number of the rule, or null for no upper bound.</param>
/// <param name="Parity">"even", "odd" or null.</param>
/// <param name="Conditions">Condition text as written in the source, or null.</param>
/// <param name="Quote">Verbatim source sentence that states the rule (at most 300 characters).</param>
/// <param name="Confidence">"low", "medium" or "high", as the model reported it.</param>
/// <param name="Note">Reviewer hint, or null.</param>
public sealed record CandidateRow(
    int Chunk,
    string SchoolAsWritten,
    long? SchoolIdPodmiot,
    string Street,
    int? NumberFrom,
    int? NumberTo,
    string? Parity,
    string? Conditions,
    string Quote,
    string Confidence,
    string? Note);

/// <summary>A row the model produced that failed validation, with the reason. Shown in the review file, never written to CSV.</summary>
public sealed record RejectedCandidate(int Chunk, string Reason);

/// <summary>Outcome of one extraction run.</summary>
public sealed record CatchmentExtractResult(
    IReadOnlyList<CandidateRow> Rows,
    IReadOnlyList<RejectedCandidate> Rejected,
    int Chunks,
    long InputTokens,
    long OutputTokens);

/// <summary>
/// Raised when the output cannot be trusted as a whole (refusal, cut-off, not one JSON object, wrong shape, too many
/// chunks, empty document). The run stops and nothing is written; a partial answer is never used.
/// </summary>
public sealed class ExtractionAbortedException(string message) : InvalidOperationException(message);

/// <summary>
/// Reads a saved gmina resolution in chunks, asks the model for candidate street rules, and validates every row
/// strictly. The model proposes; this class decides only what is well-formed and evidenced by a verbatim quote.
/// It never decides which school serves a street and never touches a database.
/// </summary>
public sealed class CatchmentExtractor(IImportLlm llm)
{
    public const int MaxChunkChars = 12_000;
    public const int MaxChunks = 40;
    public const int MaxOutputTokens = 3000;
    public const int MaxQuoteChars = 300;
    public const int MaxConditionChars = 300;
    private const int MaxNumber = 999_999;

    private static readonly string[] Confidences = ["low", "medium", "high"];
    private static readonly string[] Parities = ["even", "odd"];

    private const string SystemRules =
        "You extract municipal catchment (obwod szkoly) street rules from ONE part of a Polish council resolution.\n" +
        "Output ONLY one JSON object, with no prose and no code fence, in exactly this shape:\n" +
        "{\"rows\":[{\"school\":\"<school name exactly as written>\",\"street\":\"<street name as written, without the ul. prefix>\"," +
        "\"numberFrom\":<integer or null>,\"numberTo\":<integer or null>,\"parity\":\"even\" | \"odd\" | null," +
        "\"conditions\":\"<condition text as written, or null>\",\"quote\":\"<exact copy of the sentence that states the rule, at most 300 characters>\"," +
        "\"confidence\":\"low\" | \"medium\" | \"high\"}]}\n" +
        "Rules:\n" +
        "- Copy school names, street names and the quote character-for-character from the text. Do not translate, correct or complete them.\n" +
        "- 'parzyste' means even and 'nieparzyste' means odd. 'parzyste 2-18' gives parity \"even\", numberFrom 2, numberTo 18.\n" +
        "- A street with no house-number restriction gives numberFrom null and numberTo null.\n" +
        "- Use only schools and streets that the text names. If the part holds no catchment rule, output {\"rows\":[]}.\n" +
        "- If you are unsure of a number or a rule, still output the row with confidence \"low\". Never guess numbers.\n" +
        "- One row per (school, street, number rule).\n" +
        UntrustedText.DataOnlyRule;

    public async Task<CatchmentExtractResult> RunAsync(
        string plainText,
        string gmina,
        SchoolNameIndex? schools,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentException.ThrowIfNullOrWhiteSpace(gmina);

        var chunks = ImportDocument.Chunk(plainText, MaxChunkChars);
        if (chunks.Count == 0)
        {
            throw new ExtractionAbortedException("The input document is empty after text extraction. Nothing was written.");
        }

        if (chunks.Count > MaxChunks)
        {
            throw new ExtractionAbortedException(
                $"The document is too long for one run ({chunks.Count} parts, limit {MaxChunks}). " +
                "Split the saved file by article and run each part. Nothing was written.");
        }

        var rows = new List<CandidateRow>();
        var rejected = new List<RejectedCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long inputTokens = 0;
        long outputTokens = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunkNo = i + 1;
            var chunk = chunks[i];

            var user =
                $"Gmina TERYT: {gmina}. Part {chunkNo} of {chunks.Count} of the resolution.\n" +
                "Return the JSON object for this part.\n" +
                UntrustedText.Wrap("resolution-part", chunk);

            var reply = await llm.CompleteAsync(SystemRules, user, MaxOutputTokens, ct);
            inputTokens += reply.InputTokens;
            outputTokens += reply.OutputTokens;

            if (string.IsNullOrEmpty(reply.Text))
            {
                throw new ExtractionAbortedException(
                    $"Part {chunkNo}: the model refused or the answer was cut off. Nothing was written.");
            }

            using var document = ModelJson.TryParseObject(reply.Text)
                ?? throw new ExtractionAbortedException(
                    $"Part {chunkNo}: the answer is not one JSON object. Nothing was written.");

            if (!document.RootElement.TryGetProperty("rows", out var rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ExtractionAbortedException(
                    $"Part {chunkNo}: the answer has no 'rows' array. Nothing was written.");
            }

            foreach (var element in rowsElement.EnumerateArray())
            {
                var outcome = Validate(element, chunkNo, chunk, schools);
                if (outcome.Row is null)
                {
                    rejected.Add(new RejectedCandidate(chunkNo, outcome.Reason!));
                    continue;
                }

                var row = outcome.Row;
                var key = string.Join("|",
                    SchoolNameIndex.Key(row.SchoolAsWritten),
                    row.SchoolIdPodmiot?.ToString() ?? string.Empty,
                    StreetNormalizer.Normalize(row.Street),
                    row.NumberFrom?.ToString() ?? string.Empty,
                    row.NumberTo?.ToString() ?? string.Empty,
                    row.Parity ?? string.Empty);

                if (!seen.Add(key))
                {
                    rejected.Add(new RejectedCandidate(chunkNo, "same school, street and number rule as an earlier row; not repeated"));
                    continue;
                }

                rows.Add(row);
            }
        }

        return new CatchmentExtractResult(rows, rejected, chunks.Count, inputTokens, outputTokens);
    }

    private static (CandidateRow? Row, string? Reason) Validate(JsonElement element, int chunkNo, string chunk, SchoolNameIndex? schools)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, "row is not a JSON object");
        }

        var school = ModelJson.String(element, "school")?.Trim();
        if (string.IsNullOrWhiteSpace(school) || school.Length > 200)
        {
            return (null, "school must be a non-empty string of at most 200 characters");
        }

        var street = ModelJson.String(element, "street")?.Trim();
        if (string.IsNullOrWhiteSpace(street) || street.Length > 200 || StreetNormalizer.Normalize(street).Length == 0)
        {
            return (null, "street must contain a street name of at most 200 characters");
        }

        if (!ModelJson.TryInt(element, "numberFrom", out var numberFrom)
            || !ModelJson.TryInt(element, "numberTo", out var numberTo))
        {
            return (null, "numberFrom and numberTo must be whole numbers or null");
        }

        if ((numberFrom is not null && (numberFrom < 1 || numberFrom > MaxNumber))
            || (numberTo is not null && (numberTo < 1 || numberTo > MaxNumber)))
        {
            return (null, "house numbers must be from 1 to 999999");
        }

        if (numberFrom is int low && numberTo is int high && low > high)
        {
            return (null, "numberFrom is greater than numberTo");
        }

        if (!TryOptionalString(element, "parity", Parities, out var parity))
        {
            return (null, "parity must be \"even\", \"odd\" or null");
        }

        if (!TryOptionalString(element, "conditions", null, out var conditionsRaw))
        {
            return (null, "conditions must be text or null");
        }

        var conditions = conditionsRaw?.Trim();
        if (conditions is not null && conditions.Length > MaxConditionChars)
        {
            return (null, "conditions text is longer than 300 characters");
        }

        var quote = ModelJson.String(element, "quote")?.Trim();
        if (string.IsNullOrWhiteSpace(quote) || quote.Length > MaxQuoteChars)
        {
            return (null, "quote must be a non-empty string of at most 300 characters");
        }

        if (!UntrustedText.IsVerbatim(chunk, quote))
        {
            return (null, "quote is not found verbatim in the source part; the row is not trusted");
        }

        if (!StreetAppearsInQuote(street, quote))
        {
            return (null, "the street name does not appear in the quote");
        }

        if (!HouseNumberAppearsInQuote(numberFrom, quote) || !HouseNumberAppearsInQuote(numberTo, quote))
        {
            return (null, "a house number does not appear in the quote");
        }

        var confidence = ModelJson.String(element, "confidence");
        if (confidence is null || Array.IndexOf(Confidences, confidence) < 0)
        {
            return (null, "confidence must be \"low\", \"medium\" or \"high\"");
        }

        long? schoolId = null;
        string? note = null;
        if (schools is not null)
        {
            if (!schools.TryMatch(school, out var id, out var matchReason))
            {
                return (null, $"school '{(school.Length > 60 ? school[..60] : school)}': {matchReason}");
            }

            schoolId = id;
        }
        else
        {
            note = "schoolIdPodmiot not filled: no school list was given; fill it in before load";
        }

        if (confidence == "low")
        {
            note = note is null ? "low confidence: check the quote closely" : note + "; low confidence";
        }

        return (new CandidateRow(
            chunkNo,
            school,
            schoolId,
            street,
            numberFrom,
            numberTo,
            parity,
            conditions,
            quote,
            confidence,
            note), null);
    }

    /// <summary>
    /// Polish street names inflect ("ulicy Kwiatowej" for "Kwiatowa"), so each word of the street is matched by its stem
    /// (all but the last two letters, at least three). This is an evidence screen, not a parser; the human checks the quote.
    /// </summary>
    private static bool StreetAppearsInQuote(string street, string quote)
    {
        var normalizedQuote = StreetNormalizer.Normalize(quote);
        var words = StreetNormalizer.Normalize(street).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        foreach (var word in words)
        {
            var stemLength = Math.Min(word.Length, Math.Max(3, word.Length - 2));
            if (!normalizedQuote.Contains(word[..stemLength], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A house number (when present) must occur in the quote as a whole digit run: "2" is not found inside "12", while
    /// "2-18" holds the runs "2" and "18". A null number is not checked here.
    /// </summary>
    private static bool HouseNumberAppearsInQuote(int? number, string quote)
    {
        if (number is not int value)
        {
            return true;
        }

        var wanted = value.ToString(CultureInfo.InvariantCulture);
        var runStart = -1;
        for (var i = 0; i <= quote.Length; i++)
        {
            var isDigit = i < quote.Length && quote[i] >= '0' && quote[i] <= '9';
            if (isDigit)
            {
                if (runStart < 0)
                {
                    runStart = i;
                }
            }
            else if (runStart >= 0)
            {
                if (string.Equals(quote.Substring(runStart, i - runStart), wanted, StringComparison.Ordinal))
                {
                    return true;
                }

                runStart = -1;
            }
        }

        return false;
    }

    /// <summary>
    /// An optional string property. Absent or JSON null gives null. A non-string value, or a string outside
    /// <paramref name="allowed"/> (when given), is invalid, so the row is rejected rather than read loosely.
    /// </summary>
    private static bool TryOptionalString(JsonElement obj, string name, string[]? allowed, out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } text)
        {
            return false;
        }

        if (allowed is not null && Array.IndexOf(allowed, text) < 0)
        {
            return false;
        }

        value = text;
        return true;
    }
}
