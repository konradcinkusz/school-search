using System.Text;

namespace SchoolSearch.Import.Catchment;

/// <summary>
/// Normalises street names for catchment rows. Identical copy of src/SchoolSearch.Api/Catchment/StreetNormalizer.cs;
/// both are checked against the same golden list (tests/SchoolSearch.Import.Tests/CatchmentLoaderTests.cs).
/// Change both or neither.
/// </summary>
/// <remarks>
/// Steps: lower-case (invariant); fold Polish and common Latin diacritics with an explicit table (no ICU
/// normalisation); replace every character that is not a letter or digit by a space; collapse whitespace;
/// drop one leading street-type prefix ("ul", "ulica", "al", "aleja", "pl", "plac").
/// </remarks>
internal static class StreetNormalizer
{
    public const int MaxLength = 200;

    private const string Diacritics = "ąćęłńóśźżàáâäãåçèéêëìíîïñòôöõùúûüýÿ";
    private const string Plain = "acelnoszzaaaaaaceeeeiiiinooooouuuuyy";

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
        if (Diacritics.Length != Plain.Length)
        {
            throw new InvalidOperationException("Street diacritic table is misaligned.");
        }

        var table = new Dictionary<char, char>(Diacritics.Length);
        for (var i = 0; i < Diacritics.Length; i++)
        {
            table[Diacritics[i]] = Plain[i];
        }

        return table;
    }
}
