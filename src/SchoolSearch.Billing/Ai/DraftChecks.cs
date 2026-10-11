using System.Text.RegularExpressions;
using SchoolSearch.Billing.Claims;
using SchoolSearch.Billing.Subscriptions;

namespace SchoolSearch.Billing.Ai;

/// <summary>Why a candidate draft may not be shown. <see cref="MatchedTerm"/> is set only for a claim-linter hit.</summary>
public sealed record DraftCheckFailure(string Code, string? MatchedTerm);

/// <summary>
/// The checks a draft must pass before it is returned: the claim linter (the same rules as the profile PUT) and a digit
/// grounding check (every number in the draft must occur in the owner's facts). The linter call lives only here, so a
/// change of its namespace or shape is a one-file fix.
/// </summary>
public static partial class DraftChecks
{
    /// <summary>Null when the candidate may be shown to the owner.</summary>
    public static DraftCheckFailure? Evaluate(DraftCandidate candidate, IReadOnlyList<string> bullets)
    {
        var term = ClaimLinter.Check(candidate.Headline) ?? ClaimLinter.Check(candidate.Description);
        if (term is not null)
        {
            return new DraftCheckFailure("claim_not_allowed", term);
        }

        var facts = string.Join("\n", bullets);
        return DigitsGrounded(candidate.Headline + "\n" + candidate.Description, facts)
            ? null
            : new DraftCheckFailure("number_not_grounded", null);
    }

    /// <summary>The owner's own facts are linted the same way as profile text, so a banned claim is refused before any model call.</summary>
    public static string? FindClaimInFacts(IReadOnlyList<string> bullets) => ClaimLinter.Check(string.Join("\n", bullets));

    /// <summary>
    /// Every maximal run of digits in the text must occur as a maximal run of digits in the facts. "2024" in a draft is
    /// grounded only when the facts contain "2024". A deliberate, cheap check on digits, as in the Api's NumberGuard.
    /// </summary>
    internal static bool DigitsGrounded(string text, string facts)
    {
        var allowed = new HashSet<string>(DigitRun().Matches(facts).Select(match => match.Value), StringComparer.Ordinal);
        return DigitRun().Matches(text).All(match => allowed.Contains(match.Value));
    }

    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();
}
