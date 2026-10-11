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

    private const string GminaNote = "Nie rozpoznano gminy. Podaj siedmiocyfrowy kod TERYT, jeśli go znasz.";
    private const string QueryNote = "Nie rozpoznano słów kluczowych. Wpisz je w polu wyszukiwania.";
    private const string PublicOnlyNote = "Nie rozpoznano opcji tylko szkoły publiczne.";
    private const string DistanceNote = "Nie rozpoznano odległości. Ustaw ją w filtrach.";
    private const string DistanceClampedNote = "Odległość ograniczono do 50 km.";
    private const string TypeNote = "Nie rozpoznano typu placówki. Wybierz typ w filtrach.";

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

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

        // The question itself is checked too, so a silent or wrong model still cannot slip a ranking request through.
        if (SearchIntentGuard.MentionsRanking(question))
        {
            unsupported.Add(QualityUnsupported);
        }

        if (SearchIntentGuard.MentionsSorting(question))
        {
            unsupported.Add(SortUnsupported);
        }

        foreach (var kind in ReadKinds(root))
        {
            if (kind == QualityKind)
            {
                unsupported.Add(QualityUnsupported);
            }
            else if (kind == SortKind)
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
