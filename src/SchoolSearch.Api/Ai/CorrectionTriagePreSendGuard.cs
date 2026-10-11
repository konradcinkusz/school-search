using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Refuses free text that carries obvious personal data before it reaches a model, and the same check on model output
/// before it reaches a moderator. Shapes only: an e-mail address, a Polish phone number (9 digits, optionally with +48
/// and separators) and an 11-digit PESEL-like run. It is a cheap net, not a classifier.
/// </summary>
public static class CorrectionTriagePreSendGuard
{
    private static readonly Regex EmailShape = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex PhoneShape = new(
        @"(?<!\d)(?:\+?48[\s-]?)?\d{3}[\s-]?\d{3}[\s-]?\d{3}(?!\d)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex PeselShape = new(
        @"(?<!\d)\d{11}(?!\d)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static bool ContainsPersonalData(string text) =>
        EmailShape.IsMatch(text) || PhoneShape.IsMatch(text) || PeselShape.IsMatch(text);
}
