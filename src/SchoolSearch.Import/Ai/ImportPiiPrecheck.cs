using System.Text.RegularExpressions;

namespace SchoolSearch.Import.Ai;

/// <summary>
/// Refuses a saved document that looks like it carries pupil-level or private-person data before any model call
/// (MASTER-PROMPT section 5, AI-FEATURES section 0). A hit rejects the whole file. Only the pattern NAMES are reported,
/// never the matched text, so the refusal itself cannot leak the data.
/// </summary>
/// <remarks>
/// A deliberate, cheap screen, not a detector of every name: it catches PESEL-like numbers, e-mail addresses, formatted
/// phone numbers, column or label words for personal fields, and a bare list of five or more lines that are each exactly
/// two capitalised words (a possible pupil list). Unformatted 9-digit numbers (REGON) are not flagged.
/// </remarks>
public static partial class ImportPiiPrecheck
{
    /// <summary>Names of the patterns that matched, in a fixed order. Empty when the text passes.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hits = new List<string>();
        if (PeselLike().IsMatch(text))
        {
            hits.Add("PESEL-like number (11 digits)");
        }

        if (EmailAddress().IsMatch(text))
        {
            hits.Add("e-mail address");
        }

        if (FormattedPhone().IsMatch(text))
        {
            hits.Add("phone number");
        }

        if (PersonalLabel().IsMatch(text))
        {
            hits.Add("personal-data label (name, PESEL, citizenship or nationality)");
        }

        if (PairNameLine().Matches(text).Count >= PairNameLineThreshold)
        {
            hits.Add("list of two-word capitalised names (possible pupil list)");
        }

        return hits;
    }

    /// <summary>Lines that are exactly two capitalised words, so a bare list of names is caught. Five or more refuse the file.</summary>
    private const int PairNameLineThreshold = 5;

    [GeneratedRegex(
        "^[ \\t]*(?:[0-9]{1,2}[.)][ \\t]*)?\\p{Lu}\\p{Ll}+[ \\t]+\\p{Lu}\\p{Ll}+[ \\t]*\\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PairNameLine();

    [GeneratedRegex("(?<![0-9])[0-9]{11}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex PeselLike();

    [GeneratedRegex("[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailAddress();

    [GeneratedRegex("(?<![0-9])(\\+48[ -]?)?[0-9]{3}[ -][0-9]{3}[ -][0-9]{3}(?![0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex FormattedPhone();

    [GeneratedRegex(
        "\\b(imi[ęe]\\s+i\\s+nazwisko|nazwisko\\s+i\\s+imi[ęe]|imi[ęe]\\s+ucznia|nazwisko\\s+ucznia|pesel|obywatelstwo|narodowo[śs][ćc]|cudzoziemc)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonalLabel();
}
