using System.Text.RegularExpressions;

namespace SchoolSearch.Billing.Claims;

/// <summary>
/// Rejects wording that a paid profile may not use: superlatives, rankings, guarantees, unverifiable badges.
/// Used by the premium profile PUT and by the AI draft assistant (F6). Case-insensitive, Polish and English,
/// with word boundaries. Returns the matched text so the caller can name it in the message.
/// </summary>
public static class ClaimLinter
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Order matters: the more specific "oficjalny ranking" is tried before the bare "ranking".
    private static readonly Regex[] Rules =
    [
        new Regex(@"\bnajlepsz\w*", Options),
        new Regex(@"\bnumer\s+1(?!\w)", Options),
        new Regex(@"\bnr\s+1(?!\w)", Options),
        new Regex(@"(?<!\w)#1(?!\w)", Options),
        new Regex(@"\btop\s+10(?!\w)", Options),
        new Regex(@"\bwynik\w*\s+egzamin\w*", Options),
        new Regex(@"\blepsz[aey]\s+ni[żz](?!\w)", Options),
        new Regex(@"(?<!\d)100\s*%", Options),
        new Regex(@"\boficjaln\w*\s+ranking\w*", Options),
        new Regex(@"\branking\w*", Options),
        new Regex(@"\bgwarantuj\w*", Options),
        new Regex(@"\bguarantee\w*", Options),
        new Regex(@"\bcertyfikowan\w*", Options),
        new Regex(@"\bcertified\w*", Options),
        new Regex(@"\bzweryfikowan\w*", Options),
        new Regex(@"\bverified\w*", Options),
        new Regex(@"\bbest\b", Options),
        new Regex(@"\bnumber\s+1(?!\w)", Options),
        new Regex(@"\btop\s+ten(?!\w)", Options),
    ];

    /// <summary>
    /// Returns the first forbidden term found in <paramref name="text"/>, exactly as written in the text, or null when
    /// the text is clean. Null or blank input is clean.
    /// </summary>
    public static string? Check(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var rule in Rules)
        {
            var match = rule.Match(text);
            if (match.Success)
            {
                return match.Value;
            }
        }

        return null;
    }
}
