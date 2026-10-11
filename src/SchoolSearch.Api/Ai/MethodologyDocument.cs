using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// The methodology document embedded in the Api assembly (docs/methodology/RANKING-METHODOLOGY.md, LogicalName
/// RANKING-METHODOLOGY.md). It is split by heading: a section is cited by its heading text, and only headings that
/// exist here are accepted as citations. Text before the first heading is not citable.
/// </summary>
public sealed partial class MethodologyDocument
{
    public const string ResourceName = "RANKING-METHODOLOGY.md";

    private readonly IReadOnlyDictionary<string, string> _bodies;

    private MethodologyDocument(string text, IReadOnlyList<string> headings, IReadOnlyDictionary<string, string> bodies)
    {
        Text = text;
        Headings = headings;
        _bodies = bodies;
    }

    /// <summary>The whole document as written. Trusted operator text, sent to the model as the only source.</summary>
    public string Text { get; }

    /// <summary>Distinct normalised heading texts in document order (the citable names).</summary>
    public IReadOnlyList<string> Headings { get; }

    /// <summary>Loads the embedded resource. Throws when the resource is missing, which is a build error, not a runtime state.</summary>
    public static MethodologyDocument LoadEmbedded() => LoadEmbedded(typeof(MethodologyDocument).Assembly);

    public static MethodologyDocument LoadEmbedded(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The embedded methodology document is missing from the Api assembly.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    }

    public static MethodologyDocument Parse(string markdown)
    {
        var headings = new List<string>();
        var builders = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        string? current = null;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var match = HeadingLine().Match(line);
            if (match.Success)
            {
                current = NormalizeHeading(match.Groups[1].Value);
                if (!builders.TryGetValue(current, out var builder))
                {
                    builder = new StringBuilder();
                    builders[current] = builder;
                    headings.Add(current);
                }

                builder.AppendLine(line);
                continue;
            }

            if (current is not null)
            {
                builders[current].AppendLine(line);
            }
        }

        var bodies = builders.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
        return new MethodologyDocument(markdown, headings, bodies);
    }

    /// <summary>True when the heading exists in the document (whitespace-normalised, case-sensitive).</summary>
    public bool IsKnownHeading(string heading) => _bodies.ContainsKey(NormalizeHeading(heading));

    /// <summary>Heading lines and bodies of the given sections, joined in the given order. Used as the grounding text.</summary>
    public string SectionsText(IEnumerable<string> headings) =>
        string.Join("\n", headings.Select(heading => _bodies.GetValueOrDefault(NormalizeHeading(heading)) ?? string.Empty));

    /// <summary>Trims and collapses whitespace so that a citation matches the heading it names.</summary>
    public static string NormalizeHeading(string heading) => WhitespaceRun().Replace(heading.Trim(), " ");

    [GeneratedRegex(@"^#{1,6}[ \t]+(.+?)[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
