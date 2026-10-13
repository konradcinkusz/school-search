using System.Collections.Generic;
using System.Text.Json;

namespace SchoolSearch.Seeder;

/// <summary>Defensive readers for response fields. Each returns null (or an empty list) when the field is absent or of another type.</summary>
public static class JsonFields
{
    public static string? GetString(JsonElement obj, string name)
    {
        if (TryGet(obj, name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    public static int? GetInt(JsonElement obj, string name)
    {
        if (TryGet(obj, name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int number))
        {
            return number;
        }

        return null;
    }

    public static bool? GetBool(JsonElement obj, string name)
    {
        if (!TryGet(obj, name, out JsonElement value))
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

        return null;
    }

    /// <summary>The string items of an array property. Non-string items are skipped.</summary>
    public static List<string> GetStrings(JsonElement obj, string name)
    {
        var result = new List<string>();
        if (TryGet(obj, name, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
        {
            AddStrings(value, result);
        }

        return result;
    }

    /// <summary>
    /// Messages from an error body: the "error" string, and the "errors" property as either a string array or an
    /// object of string arrays (ASP.NET ModelState style).
    /// </summary>
    public static List<string> Messages(JsonElement obj)
    {
        var messages = new List<string>();
        string? single = GetString(obj, "error");
        if (single is not null)
        {
            messages.Add(single);
        }
        else if (TryGet(obj, "error", out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
        {
            // Reviews and Billing answer { "error": { "code", "message", "traceId" } }.
            string? nestedMessage = GetString(nested, "message");
            if (nestedMessage is not null)
            {
                messages.Add(nestedMessage);
            }
        }

        if (TryGet(obj, "errors", out JsonElement errors))
        {
            if (errors.ValueKind == JsonValueKind.Array)
            {
                AddStrings(errors, messages);
            }
            else if (errors.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        AddStrings(property.Value, messages);
                    }
                }
            }
        }

        return messages;
    }

    private static void AddStrings(JsonElement array, List<string> into)
    {
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                into.Add(item.GetString() ?? string.Empty);
            }
        }
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
        {
            return obj.TryGetProperty(name, out value);
        }

        value = default;
        return false;
    }
}
