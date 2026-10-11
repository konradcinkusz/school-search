using System.Text.Json;
using System.Text.RegularExpressions;

namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// Output checks for the agent's reply (ADR-0011 output guard, applied here to tool results): every digit run in the reply
/// must occur in a tool result or in the request's gmina; the catchment disclaimer is present when a catchment answer was
/// used; and the safe fallback lists only facts the tools returned.
/// </summary>
public static class AgentGrounding
{
    private static readonly Regex DigitRun = new("[0-9]+", RegexOptions.CultureInvariant);

    /// <summary>Every digit run in the text, as written (a decimal comma splits into two runs).</summary>
    public static HashSet<string> DigitRunsIn(string text) =>
        DigitRun.Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>True when every digit run of the reply occurs in one of the sources or in the gmina code.</summary>
    public static bool IsGrounded(string reply, IEnumerable<string> sources, string? gmina)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            allowed.UnionWith(DigitRunsIn(source));
        }

        if (gmina is not null)
        {
            allowed.UnionWith(DigitRunsIn(gmina));
        }

        return DigitRunsIn(reply).IsSubsetOf(allowed);
    }

    /// <summary>Adds the catchment disclaimer when the text does not already carry it.</summary>
    public static string EnsureCatchmentDisclaimer(string text) =>
        text.Contains("obwód ustala gmina", StringComparison.OrdinalIgnoreCase)
            ? text
            : text + "\n\n" + AgentPrompts.CatchmentDisclaimer;

    /// <summary>Every slug (from the tool results) that the reply names as a whole token.</summary>
    public static IReadOnlyList<string> CitedSlugs(string reply, IEnumerable<string> slugs)
    {
        var cited = new List<string>();
        foreach (var slug in slugs)
        {
            var pattern = "(?<![A-Za-z0-9-])" + Regex.Escape(slug) + "(?![A-Za-z0-9-])";
            if (Regex.IsMatch(reply, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                cited.Add(slug);
            }
        }

        return cited;
    }

    /// <summary>All distinct "slug" values in the tool results, in order of first appearance.</summary>
    public static IReadOnlyList<string> CollectSlugs(IEnumerable<string> jsonTexts)
    {
        var slugs = new List<string>();
        foreach (var json in jsonTexts)
        {
            using var document = TryParse(json);
            if (document is null)
            {
                continue;
            }

            foreach (var obj in Objects(document.RootElement))
            {
                if (StringProperty(obj, "slug") is { } slug && !slugs.Contains(slug))
                {
                    slugs.Add(slug);
                }
            }
        }

        return slugs;
    }

    /// <summary>Distinct (name, slug) pairs of the tool results: schools and preschools as the catalog names them.</summary>
    public static IReadOnlyList<(string Name, string Slug)> CollectSchools(IEnumerable<string> jsonTexts)
    {
        var found = new List<(string Name, string Slug)>();
        foreach (var json in jsonTexts)
        {
            using var document = TryParse(json);
            if (document is null)
            {
                continue;
            }

            foreach (var obj in Objects(document.RootElement))
            {
                if (StringProperty(obj, "name") is { } name
                    && StringProperty(obj, "slug") is { } slug
                    && !found.Contains((name, slug)))
                {
                    found.Add((name, slug));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The safe reply when the model's own text cannot be shown: the lead sentence and the schools the tools returned,
    /// with their names and slugs only.
    /// </summary>
    public static string Fallback(string lead, IEnumerable<string> jsonTexts)
    {
        var schools = CollectSchools(jsonTexts).Take(10).ToList();
        var lines = new List<string> { lead };
        if (schools.Count == 0)
        {
            lines.Add("W katalogu nie znaleziono danych, które odpowiadają na to pytanie.");
        }

        foreach (var (name, slug) in schools)
        {
            lines.Add("- " + name + " (slug: " + slug + ")");
        }

        return string.Join("\n", lines);
    }

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringProperty(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<JsonElement> Objects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            {
                foreach (var nested in Objects(property.Value))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in Objects(item))
                {
                    yield return nested;
                }
            }
        }
    }
}
