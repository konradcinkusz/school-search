using System.Text;

namespace SchoolSearch.Api.Catchment;

/// <summary>
/// Normalises street names for catchment lookup. The importer has an identical copy
/// (src/SchoolSearch.Import/Catchment/StreetNormalizer.cs); both are checked against the same golden list
/// (tests/SchoolSearch.Import.Tests/CatchmentLoaderTests.cs). Change both or neither.
/// </summary>
/// <remarks>
/// Steps: lower-case (invariant); fold Polish and common Latin diacritics with an explicit table (no ICU
/// normalisation, so the result does not depend on the host's globalisation settings); replace every
/// character that is not a letter or digit by a space; collapse whitespace; drop one leading street-type
/// prefix ("ul", "ulica", "al", "aleja", "pl", "plac").
/// </remarks>
internal static class StreetNormalizer
{
    public const int MaxLength = 200;

    /// <summary>Polish and common Latin letters and their plain forms. Explicit pairs, so no alignment can drift.</summary>
    private static readonly (char From, char To)[] FoldPairs =
    [
        ('ą','a'), ('ć','c'), ('ę','e'), ('ł','l'), ('ń','n'), ('ó','o'), ('ś','s'), ('ź','z'), ('ż','z'),
        ('à','a'), ('á','a'), ('â','a'), ('ä','a'), ('ã','a'), ('å','a'), ('ç','c'),
        ('è','e'), ('é','e'), ('ê','e'), ('ë','e'), ('ì','i'), ('í','i'), ('î','i'), ('ï','i'),
        ('ñ','n'), ('ò','o'), ('ô','o'), ('ö','o'), ('õ','o'), ('ù','u'), ('ú','u'), ('û','u'),
        ('ü','u'), ('ý','y'), ('ÿ','y')
    ];

    private static readonly string[] Prefixes = ["ul", "ulica", "al", "aleja", "pl", "plac"];

    private static readonly Dictionary<char, char> FoldTable = BuildFoldTable();

    /// <summary>The normalised street, or an empty string when nothing is left (for example "ul.").</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var lower = raw.ToLowerInvariant();
        var folded = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            if (FoldTable.TryGetValue(c, out var plain))
            {
                folded.Append(plain);
            }
            else if (char.IsLetterOrDigit(c))
            {
                folded.Append(c);
            }
            else
            {
                folded.Append(' ');
            }
        }

        var tokens = folded.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = tokens.Length > 0 && Array.IndexOf(Prefixes, tokens[0]) >= 0 ? 1 : 0;

        return string.Join(" ", tokens, start, tokens.Length - start);
    }

    private static Dictionary<char, char> BuildFoldTable()
    {
        var table = new Dictionary<char, char>(FoldPairs.Length);
        foreach (var (from, to) in FoldPairs)
        {
            table[from] = to;
        }

        return table;
    }
}
