using System.Text.Json;
using System.Text.Json.Nodes;
using SchoolSearch.Import.Ai;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// A scripted stand-in for the operator-side model seam <see cref="IImportLlm"/>. Each reply comes from the supplied
/// function, and every system and user turn is recorded so a test can check what would have been sent. No network.
/// </summary>
public sealed class ExtractorScriptedLlm(Func<ImportLlmReply> reply) : IImportLlm
{
    private readonly List<(string System, string User)> _calls = [];

    /// <summary>Every (system, user) pair the extractor sent, in order.</summary>
    public IReadOnlyList<(string System, string User)> Calls => _calls;

    public Task<ImportLlmReply> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        _calls.Add((system, user));
        return Task.FromResult(reply());
    }
}

/// <summary>JSON helpers shared by the extractor evals. Unlike <see cref="EvalJson"/>, a JSON null reads as null here.</summary>
public static class ExtractorSupport
{
    /// <summary>A string property, or null when absent or JSON null.</summary>
    public static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>An integer property, or null when absent or JSON null.</summary>
    public static int? Int(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    /// <summary>A 64-bit integer property, or null when absent or JSON null.</summary>
    public static long? Long(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return null;
    }

    /// <summary>The strings of an array property. Empty when absent.</summary>
    public static IReadOnlyList<string> Strings(JsonElement obj, string name)
    {
        var items = new List<string>();
        if (obj.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                items.Add(item.GetString() ?? string.Empty);
            }
        }

        return items;
    }

    /// <summary>The integers of an array property. Empty when absent.</summary>
    public static IReadOnlyList<long> Longs(JsonElement obj, string name)
    {
        var items = new List<long>();
        if (obj.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                items.Add(item.GetInt64());
            }
        }

        return items;
    }

    /// <summary>The objects of an array property. Empty when absent.</summary>
    public static IReadOnlyList<JsonElement> Objects(JsonElement obj, string name)
    {
        var items = new List<JsonElement>();
        if (obj.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            items.AddRange(array.EnumerateArray());
        }

        return items;
    }

    /// <summary>The object held by an object-valued property, as a list of zero or one. Empty when absent or not an object.</summary>
    public static IReadOnlyList<JsonElement> SingleObject(JsonElement obj, string name)
    {
        var items = new List<JsonElement>();
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
        {
            items.Add(value);
        }

        return items;
    }

    /// <summary>A mutable copy of a JSON object, so a reply can be built from dataset text.</summary>
    public static JsonObject ParseObject(string raw) => (JsonObject)JsonNode.Parse(raw)!;

    /// <summary>
    /// The model's view of a gold row: the gold fields without the id, with the conditions null and a high confidence.
    /// This is an honest answer, so the extractor must accept it.
    /// </summary>
    public static JsonObject ModelRowFromGold(JsonElement gold)
    {
        var row = ParseObject(gold.GetRawText());
        row.Remove("idPodmiot");
        row["conditions"] = null;
        row["confidence"] = JsonValue.Create("high");
        return row;
    }

    /// <summary>A reply shaped as {"rows":[...]}, the contract both extractors read.</summary>
    public static string ModelReply(IEnumerable<JsonObject> rows)
    {
        var array = new JsonArray();
        foreach (var row in rows)
        {
            array.Add(row);
        }

        var root = new JsonObject();
        root["rows"] = array;
        return root.ToJsonString();
    }

    /// <summary>Digit runs of a text, as a set, for number grounding checks.</summary>
    public static IReadOnlySet<string> DigitRuns(string text) =>
        new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(text, "[0-9]+").Select(match => match.Value), StringComparer.Ordinal);
}
