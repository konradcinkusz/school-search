using System.Text.Json;

namespace SchoolSearch.Evals.Tests;

/// <summary>Typed, strict readers over the JSON of a dataset case. A wrong kind or a missing required field throws <see cref="FormatException"/>.</summary>
public static class EvalJson
{
    public static string RequireString(JsonElement obj, string name) =>
        OptionalString(obj, name) ?? throw new FormatException($"Missing required string '{name}'.");

    public static string? OptionalString(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new FormatException($"'{name}' must be a string.");
        }

        return value.GetString();
    }

    public static bool RequireBool(JsonElement obj, string name) =>
        OptionalBool(obj, name) ?? throw new FormatException($"Missing required boolean '{name}'.");

    /// <summary>False when the field is absent.</summary>
    public static bool Flag(JsonElement obj, string name) => OptionalBool(obj, name) ?? false;

    public static bool? OptionalBool(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        throw new FormatException($"'{name}' must be a boolean.");
    }

    public static int? OptionalInt(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new FormatException($"'{name}' must be an integer.");
        }

        return number;
    }

    /// <summary>Empty when the field is absent.</summary>
    public static IReadOnlyList<string> StringArray(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value))
        {
            return Array.Empty<string>();
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"'{name}' must be an array of strings.");
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"'{name}' must contain only strings.");
            }

            items.Add(item.GetString() ?? string.Empty);
        }

        return items;
    }

    public static JsonElement RequireObject(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Missing required object '{name}'.");
        }

        return value;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"Expected a JSON object when reading '{name}'.");
        }

        return obj.TryGetProperty(name, out value);
    }
}
