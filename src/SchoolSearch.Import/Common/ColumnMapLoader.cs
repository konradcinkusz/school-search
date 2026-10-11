using System.Text.Json;

namespace SchoolSearch.Import;

/// <summary>
/// Reads an optional JSON override for a column map. Without a file the type's built-in defaults apply
/// (the defaults are UNVERIFIED until checked against a real export: see docs/runbooks/IMPORT.md).
/// Unknown JSON properties are an error, so a misspelt override cannot silently fall back to a default.
/// </summary>
public static class ColumnMapLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static T Load<T>(string? jsonPath) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(jsonPath))
        {
            return new T();
        }

        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException("Column map override file not found.", jsonPath);
        }

        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new InvalidDataException($"Column map override '{jsonPath}' is empty.");
    }
}
