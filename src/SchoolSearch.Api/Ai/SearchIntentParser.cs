using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>What the parser accepted from a model answer. Every value here passed validation.</summary>
public sealed record SearchIntentParseResult(SearchIntentFilters Filters, IReadOnlyList<string> Unsupported);

/// <summary>
/// Turns the model's JSON answer into validated filters. Defensive: anything that is not a JSON object yields null
/// (the service answers ai_output_rejected), a field of the wrong type or shape is dropped with a note, and no
/// value is guessed. Quality and sort words are routed to <c>Unsupported</c>, never into a filter.
/// </summary>
public static partial class SearchIntentParser
{
    public const string QualityUnsupported = "Portal nie układa szkół według jakości. Wyniki są w stałej kolejności, a ocen jakości szkół nie podajemy.";
    public const string SortUnsupported = "Sortowanie według wyniku nie jest dostępne. Wyniki są w stałej kolejności.";

    public const double MaxDistanceKm = 50;

    private const int MaxQueryLength = 80;
    private const int MaxNotes = 6;
    private const string QualityKind = "quality_or_ranking";
    private const string SortKind = "sort_order";
    private const string NurseryKind = "nursery";

    private const string GminaNote = "Nie rozpoznano gminy. Podaj siedmiocyfrowy kod TERYT, jeśli go znasz.";
    private const string QueryNote = "Nie rozpoznano słów kluczowych. Wpisz je w polu wyszukiwania.";
    private const string PublicOnlyNote = "Nie rozpoznano opcji tylko szkoły publiczne.";
    private const string DistanceNote = "Nie rozpoznano odległości. Ustaw ją w filtrach.";
    private const string DistanceClampedNote = "Odległość ograniczono do 50 km.";
    public const string KindUnsupported = "Nie rozpoznano rodzaju placówki. Wybierz szkoły, przedszkola albo wszystko w filtrach.";
    public const string NurseryUnsupported = "Żłobki nie są jeszcze obsługiwane. Możesz szukać szkół i przedszkoli.";
    public const string TuitionUnsupported = "Nie rozpoznano kwoty czesnego. Ustaw ją w filtrach (od 0 do 20000 zł).";
    public const string FreePlacesUnsupported = "Nie rozpoznano opcji wolnych miejsc.";
    public const string PlaceUnsupported = "Nie rozpoznano miejsca. Wpisz nazwę miejscowości, ulicy lub placu, bez współrzędnych i danych osobowych.";

    public const int MaxTuitionPln = 20000;
    public const int MaxPlaceLength = 120;

    private const string TypeNote = "Nie rozpoznano typu placówki. Wybierz typ w filtrach.";

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"(?<!\d)\d{6,}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongDigitRun();

    [GeneratedRegex(@"\d{1,3}[.,]\d{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex PreciseDecimal();

    [GeneratedRegex(@"-?\d{1,3}[.,]\d+\s*[,;/ ]\s*-?\d{1,3}[.,]\d+", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPair();

    [GeneratedRegex(@"\b(?:lat|lon|lng|long|latitude|longitude|coords?|szer|dług)\b|°", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CoordinateWords();

    [GeneratedRegex(@"żłob|zlob|nursery|creche", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NurseryWords();

    [GeneratedRegex(@"^\p{Lu}\p{Ll}+(?:-\p{Lu}\p{Ll}+)?\s\p{Lu}\p{Ll}+(?:-\p{Lu}\p{Ll}+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TwoCapitalWords();

    [GeneratedRegex(@"\b(?:plac|pl|ulica|ul|aleja|al|osiedle|os|rynek|park|dworzec|centrum|gmina|miasto|dzielnica|most|rondo)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceKeywords();

    /// <summary>Null when the text is not a JSON object. Never throws.</summary>
    public static SearchIntentParseResult? Parse(string modelText, string question)
    {
        var json = ExtractObject(modelText);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? Build(document.RootElement, question)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The span from the first '{' to the last '}', so a fenced or prefixed answer still parses.</summary>
    internal static string? ExtractObject(string modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return null;
        }

        var start = modelText.IndexOf('{');
        var end = modelText.LastIndexOf('}');
        return start < 0 || end <= start ? null : modelText[start..(end + 1)];
    }

    private static SearchIntentParseResult Build(JsonElement root, string question)
    {
        var notes = new List<string>();
        var unsupported = new List<string>();

        var gmina = ReadGmina(root, question, notes);
        var query = ReadQuery(root, notes, unsupported);
        var publicOnly = ReadPublicOnly(root, notes);
        var distance = ReadDistance(root, notes);
        var type = ReadType(root, notes);
        var kind = ReadKind(root, unsupported);
        var tuition = ReadMaxTuition(root, unsupported);
        var freePlaces = ReadHasFreePlaces(root, unsupported);
        var place = ReadPlace(root, unsupported);

        if (NurseryWords().IsMatch(question))
        {
            unsupported.Add(NurseryUnsupported);
        }

        // The question itself is checked too, so a silent or wrong model still cannot slip a ranking request through.
        if (SearchIntentGuard.MentionsRanking(question))
        {
            unsupported.Add(QualityUnsupported);
        }

        if (SearchIntentGuard.MentionsSorting(question))
        {
            unsupported.Add(SortUnsupported);
        }

        foreach (var unsupportedKind in ReadKinds(root))
        {
            if (unsupportedKind == NurseryKind)
            {
                unsupported.Add(NurseryUnsupported);
            }
            else if (unsupportedKind == QualityKind)
            {
                unsupported.Add(QualityUnsupported);
            }
            else if (unsupportedKind == SortKind)
            {
                unsupported.Add(SortUnsupported);
            }
        }

        var filters = new SearchIntentFilters(
            gmina,
            query,
            publicOnly,
            distance,
            type,
            kind,
            tuition,
            freePlaces,
            place,
            notes.Distinct(StringComparer.Ordinal).Take(MaxNotes).ToArray());

        return new SearchIntentParseResult(filters, unsupported.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Accepted only when it is exactly seven digits and the same digits appear in the parent's question.</summary>
    private static string? ReadGmina(JsonElement root, string question, List<string> notes)
    {
        if (!TryGetValue(root, "gmina", out var element))
        {
            return null;
        }

        var value = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim() : null;
        if (value is { Length: 7 }
            && value.All(char.IsAsciiDigit)
            && DigitRun().Matches(question).Any(match => match.Value == value))
        {
            return value;
        }

        notes.Add(GminaNote);
        return null;
    }

    private static string? ReadQuery(JsonElement root, List<string> notes, List<string> unsupported)
    {
        if (!TryGetValue(root, "q", out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            notes.Add(QueryNote);
            return null;
        }

        var value = SearchIntentGuard.Collapse(element.GetString() ?? string.Empty);
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Length > MaxQueryLength || value.Contains('<') || value.Contains('>') || SearchIntentGuard.ContainsPersonalData(value))
        {
            notes.Add(QueryNote);
            return null;
        }

        if (SearchIntentGuard.MentionsRanking(value))
        {
            unsupported.Add(QualityUnsupported);
            return null;
        }

        if (SearchIntentGuard.MentionsSorting(value))
        {
            unsupported.Add(SortUnsupported);
            return null;
        }

        return value;
    }

    /// <summary>Only an explicit true sets the flag; false means "no restriction", which is the same as not setting it.</summary>
    private static bool? ReadPublicOnly(JsonElement root, List<string> notes)
    {
        if (!TryGetValue(root, "publicOnly", out var element))
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return null;
            default:
                notes.Add(PublicOnlyNote);
                return null;
        }
    }

    private static double? ReadDistance(JsonElement root, List<string> notes)
    {
        if (!TryGetValue(root, "maxDistanceKm", out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out var km)
            && double.IsFinite(km)
            && km > 0)
        {
            if (km > MaxDistanceKm)
            {
                notes.Add(DistanceClampedNote);
            }

            return Math.Min(km, MaxDistanceKm);
        }

        notes.Add(DistanceNote);
        return null;
    }

    private static string? ReadType(JsonElement root, List<string> notes)
    {
        if (!TryGetValue(root, "type", out var element))
        {
            return null;
        }

        var value = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim().ToLowerInvariant() : null;
        if (value is "school" or "preschool")
        {
            return value;
        }

        notes.Add(TypeNote);
        return null;
    }

    /// <summary>school, preschool or all (case and space insensitive). A nursery value gets its own sentence; anything else is unsupported.</summary>
    private static string? ReadKind(JsonElement root, List<string> unsupported)
    {
        if (!TryGetValue(root, "kind", out var element))
        {
            return null;
        }

        var value = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim().ToLowerInvariant() : null;
        if (value is "school" or "preschool" or "all")
        {
            return value;
        }

        unsupported.Add(value is not null && NurseryWords().IsMatch(value) ? NurseryUnsupported : KindUnsupported);
        return null;
    }

    /// <summary>Only a JSON integer from 0 to 20000. Strings, fractions and out-of-range numbers are never coerced.</summary>
    private static int? ReadMaxTuition(JsonElement root, List<string> unsupported)
    {
        if (!TryGetValue(root, "maxTuitionPln", out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var value)
            && value is >= 0 and <= MaxTuitionPln)
        {
            return value;
        }

        unsupported.Add(TuitionUnsupported);
        return null;
    }

    /// <summary>Only an explicit true sets the flag; false means "no restriction"; any other type is unsupported.</summary>
    private static bool? ReadHasFreePlaces(JsonElement root, List<string> unsupported)
    {
        if (!TryGetValue(root, "hasFreePlaces", out var element))
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return null;
            default:
                unsupported.Add(FreePlacesUnsupported);
                return null;
        }
    }

    /// <summary>
    /// A short place phrase. Coordinates, personal data, markup and lists of people are rejected whole, never cleaned up.
    /// </summary>
    private static string? ReadPlace(JsonElement root, List<string> unsupported)
    {
        if (!TryGetValue(root, "place", out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            unsupported.Add(PlaceUnsupported);
            return null;
        }

        var value = SearchIntentGuard.Collapse(element.GetString() ?? string.Empty);
        if (value.Length == 0)
        {
            return null;
        }

        if (SearchIntentGuard.MentionsRanking(value))
        {
            unsupported.Add(QualityUnsupported);
            return null;
        }

        if (value.Length > MaxPlaceLength
            || value.Contains('<')
            || value.Contains('>')
            || !value.Any(char.IsLetter)
            || SearchIntentGuard.ContainsPersonalData(value)
            || LongDigitRun().IsMatch(value)
            || LooksLikeCoordinates(value)
            || LooksLikeNameList(value))
        {
            unsupported.Add(PlaceUnsupported);
            return null;
        }

        return value;
    }

    private static bool LooksLikeCoordinates(string value) =>
        DecimalPair().IsMatch(value) || PreciseDecimal().IsMatch(value) || CoordinateWords().IsMatch(value);

    /// <summary>Two or more comma, semicolon or "i/oraz" separated parts that are each "Imię Nazwisko" and name no place word.</summary>
    private static bool LooksLikeNameList(string value)
    {
        if (PlaceKeywords().IsMatch(value))
        {
            return false;
        }

        var parts = Regex.Split(value, @"\s*[,;]\s*|\s+(?:i|oraz|and)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length >= 2 && parts.All(part => TwoCapitalWords().IsMatch(part));
    }

    private static List<string> ReadKinds(JsonElement root)
    {
        var kinds = new List<string>();
        if (!root.TryGetProperty("unsupportedKinds", out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return kinds;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } kind)
            {
                kinds.Add(kind.Trim());
            }
        }

        return kinds;
    }

    /// <summary>False when the property is absent or null, so "not given" and "null" are handled alike.</summary>
    private static bool TryGetValue(JsonElement root, string name, out JsonElement element)
    {
        if (root.TryGetProperty(name, out element)
            && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            return true;
        }

        element = default;
        return false;
    }
}
