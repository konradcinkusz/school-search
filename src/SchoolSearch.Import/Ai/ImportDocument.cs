using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace SchoolSearch.Import.Ai;

/// <summary>
/// Turns a SAVED document (plain text, CSV or HTML) into the text the model reads, and splits it into chunks. The same
/// text is the source for quote verification, so a quote is checked against exactly what the model saw.
/// </summary>
/// <remarks>
/// Nothing is fetched or scraped: the operator saves the page or resolution first and passes the file. Chunking never
/// drops text: every character of the source is in exactly one chunk (paragraph boundaries where possible, otherwise a
/// whitespace boundary, otherwise a hard cut).
/// </remarks>
public static partial class ImportDocument
{
    public const int MaxInputBytes = 5 * 1024 * 1024;

    /// <summary>Plain text from a saved file. HTML is reduced to readable lines; anything else is returned as read.</summary>
    public static string ToPlainText(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (!LooksLikeHtml(raw))
        {
            return NormalizeLines(raw);
        }

        var text = ScriptOrStyle().Replace(raw, " ");
        text = BlockBreak().Replace(text, "\n");
        text = CellBreak().Replace(text, " | ");
        text = AnyTag().Replace(text, string.Empty);
        return NormalizeLines(WebUtility.HtmlDecode(text));
    }

    /// <summary>
    /// Splits text into chunks of at most <paramref name="maxChars"/> characters. Concatenating the chunks returns the
    /// input with only whitespace at chunk edges changed.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxChars < 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars), "A chunk must hold at least 200 characters.");
        }

        var chunks = new List<string>();
        var rest = text.Trim();

        while (rest.Length > 0)
        {
            if (rest.Length <= maxChars)
            {
                chunks.Add(rest);
                break;
            }

            var cut = rest.LastIndexOf("\n\n", maxChars, StringComparison.Ordinal);
            if (cut < maxChars / 2)
            {
                cut = rest.LastIndexOf('\n', maxChars);
            }

            if (cut < maxChars / 2)
            {
                cut = rest.LastIndexOf(' ', maxChars);
            }

            if (cut < maxChars / 2)
            {
                cut = maxChars;
            }

            chunks.Add(rest[..cut].Trim());
            rest = rest[cut..].TrimStart();
        }

        return chunks.Where(c => c.Length > 0).ToList();
    }

    private static bool LooksLikeHtml(string raw)
    {
        var head = raw.Length > 4000 ? raw[..4000] : raw;
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<table", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<p>", StringComparison.OrdinalIgnoreCase)
            || head.Contains("<div", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLines(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => SpaceRun().Replace(line, " ").Trim());

        return string.Join("\n", lines).Trim();
    }

    [GeneratedRegex("<(script|style)\\b[^>]*>.*?</\\1\\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex("<br\\s*/?>|</(p|div|li|tr|h[1-6]|section|article)\\s*>|<(p|div|li|tr|h[1-6])\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockBreak();

    [GeneratedRegex("</t[dh]\\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CellBreak();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex AnyTag();

    [GeneratedRegex("[ \\t\\f\\v\\u00A0]+", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRun();
}
