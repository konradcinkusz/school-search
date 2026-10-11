using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Digit-sequence grounding check. Every maximal run of digits in the model text must occur as a maximal run of
/// digits in the facts payload. "-3,75" is grounded by facts containing "-3.75" (runs "3" and "75"); "98765" is not.
/// </summary>
/// <remarks>
/// This is a deliberate, cheap check on the digits, not a value parser: a run can match a number that appears in
/// another field. It rejects the failure it is for, a number the Api never computed, and nothing else.
/// </remarks>
public static partial class NumberGuard
{
    [GeneratedRegex("[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

    public static bool IsGrounded(string text, string factsJson)
    {
        var allowed = new HashSet<string>(
            DigitRun().Matches(factsJson).Select(match => match.Value),
            StringComparer.Ordinal);

        return DigitRun().Matches(text).All(match => allowed.Contains(match.Value));
    }
}
