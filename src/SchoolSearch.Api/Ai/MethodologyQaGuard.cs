using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Pre-send guard for free text (ADR-0011 data rules): refuses obvious e-mail addresses, Polish phone numbers and
/// 11-digit PESEL-like runs before any model call. It is a heuristic and only refuses; it never edits the text.
/// </summary>
public static partial class MethodologyQaGuard
{
    public static bool ContainsPersonalData(string text) =>
        EmailPattern().IsMatch(text) || PhonePattern().IsMatch(text) || PeselPattern().IsMatch(text);

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?\d{2}[ \-]?)?\d{3}[ \-]?\d{3}[ \-]?\d{3}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PeselPattern();
}
