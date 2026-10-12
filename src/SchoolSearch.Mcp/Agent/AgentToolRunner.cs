using System.Text.Json;
using ModelContextProtocol.Protocol;
using SchoolSearch.Mcp.Tools;

namespace SchoolSearch.Mcp.Agent;

/// <summary>The result of one agent tool call: the JSON the model sees, a short Polish summary for the user, and the error flag.</summary>
public sealed record AgentToolOutcome(string ContentJson, string Summary, bool IsError);

/// <summary>
/// Runs the agent's tool calls in-process through the same tool classes the MCP surface uses, so the validation, the
/// clamping and the error shapes are identical. Arguments are the model's JSON; a wrong or missing type becomes a null
/// and the tool's own validation answers it.
/// </summary>
public sealed class AgentToolRunner(SchoolTools schools, PreschoolTools preschools, ReviewsTools reviews)
{
    public async Task<AgentToolOutcome> RunAsync(string name, string argumentsJson, CancellationToken ct)
    {
        var args = ParseArguments(argumentsJson);

        CallToolResult? result = name switch
        {
            "find_schools" => await schools.FindSchools(
                query: Text(args, "query"),
                gmina: Text(args, "gmina"),
                isPublic: Flag(args, "isPublic"),
                lat: Number(args, "lat"),
                lon: Number(args, "lon"),
                radiusKm: Number(args, "radiusKm"),
                sort: Text(args, "sort"),
                page: Integer(args, "page"),
                pageSize: Integer(args, "pageSize"),
                ct: ct),
            "get_school" => await schools.GetSchool(Int64(args, "rspo") ?? 0, ct),
            "get_catchment_school" => await schools.GetCatchmentSchool(
                Text(args, "gmina") ?? string.Empty,
                Text(args, "street") ?? string.Empty,
                Text(args, "number"),
                ct),
            "compare_schools" => await schools.CompareSchools(Strings(args, "slugs") ?? [], ct),
            "get_ranking" => await schools.GetRanking(
                Text(args, "scope") ?? string.Empty,
                Text(args, "gmina"),
                Integer(args, "year"),
                Integer(args, "page"),
                Integer(args, "pageSize"),
                ct),
            "get_methodology" => await schools.GetMethodology(ct),
            "find_preschools" => await preschools.FindPreschools(
                q: Text(args, "q"),
                gmina: Text(args, "gmina"),
                publicOnly: Flag(args, "publicOnly"),
                maxTuition: Number(args, "maxTuition"),
                hasFreePlaces: Flag(args, "hasFreePlaces"),
                lat: Number(args, "lat"),
                lon: Number(args, "lon"),
                radiusKm: Number(args, "radiusKm"),
                page: Integer(args, "page"),
                pageSize: Integer(args, "pageSize"),
                ct: ct),
            "get_preschool" => await preschools.GetPreschool(Text(args, "slug") ?? string.Empty, ct),
            "get_school_reviews" => await reviews.GetSchoolReviews(
                Text(args, "slug") ?? string.Empty,
                Integer(args, "page"),
                Integer(args, "pageSize"),
                ct),
            _ => null,
        };

        if (result is null)
        {
            var json = JsonSerializer.Serialize(new { error = new { code = "unknown_tool", message = "Nieznane narzędzie." } });
            return new AgentToolOutcome(json, "Nieznane narzędzie.", true);
        }

        var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        var isError = result.IsError == true;
        return new AgentToolOutcome(text, Summarize(text, isError), isError);
    }

    /// <summary>The model's arguments as a JSON object. Anything else (or invalid JSON) becomes an empty object.</summary>
    public static JsonElement ParseArguments(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
            // Fall through to the empty object: the tool's validation answers the missing arguments.
        }

        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    /// <summary>A short Polish line for the user's step list. It never quotes review text.</summary>
    private static string Summarize(string json, bool isError)
    {
        if (isError)
        {
            return ToolResponse.TryReadEnvelope(json, out var code, out _) ? "Błąd: " + code + "." : "Błąd narzędzia.";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return "Pobrano dane.";
            }

            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return "Pozycji: " + items.GetArrayLength() + ".";
            }

            if (root.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.String)
            {
                return MatchSummary(match.GetString());
            }

            if (root.TryGetProperty("slug", out var slug) && slug.ValueKind == JsonValueKind.String
                && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                return "Profil: " + name.GetString() + ".";
            }
        }
        catch (JsonException)
        {
            // A non-JSON body (not expected from the tools) gets the generic line.
        }

        return "Pobrano dane.";
    }

    private static string MatchSummary(string? match) => match switch
    {
        "street_list_match" => "Ulica jest na liście obwodu.",
        "no_match" => "Ulicy nie ma na liście obwodu.",
        "no_number_match" => "Ulica jest na liście obwodu, ale żaden jej przedział nie obejmuje tego numeru.",
        "no_data" => "Gmina nie opublikowała listy ulic.",
        _ => "Sprawdzono obwód.",
    };

    private static string? Text(JsonElement args, string key) =>
        args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Flag(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        return value.ValueKind == JsonValueKind.False ? false : null;
    }

    private static double? Number(JsonElement args, string key)
    {
        if (args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        return null;
    }

    private static int? Integer(JsonElement args, string key)
    {
        if (args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return null;
    }

    private static long? Int64(JsonElement args, string key)
    {
        if (args.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return null;
    }

    private static string[]? Strings(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }
}
