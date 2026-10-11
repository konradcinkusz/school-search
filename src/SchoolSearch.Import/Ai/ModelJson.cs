using System.Text.Json;

namespace SchoolSearch.Import.Ai;

/// <summary>
/// Defensive reading of a model answer that should be one JSON object. Code fences and prose around the object are
/// tolerated; anything else is a parse failure, and a failure never yields partial trust (the caller fails the chunk).
/// </summary>
public static class ModelJson
{
    /// <summary>The top-level object, or null when the text holds no single well-formed JSON object. Caller disposes.</summary>
    public static JsonDocument? TryParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(text[start..(end + 1)]);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A string property with the given name, or null when absent or not a string.</summary>
    public static string? String(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// An integer property: null when absent or JSON null. Returns false (invalid) for a non-integer value, so the caller
    /// can reject the row instead of guessing.
    /// </summary>
    public static bool TryInt(JsonElement obj, string name, out int? value)
    {
        value = null;
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
        {
            value = number;
            return true;
        }

        return false;
    }
}
