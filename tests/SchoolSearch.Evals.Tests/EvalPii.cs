using System.Text.RegularExpressions;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Shape checks for personal data in dataset text: an e-mail address, a Polish telephone number (9 digits with optional
/// +48 and separators), an 11-digit PESEL-like run and a NN-NNN postal code. The phone and e-mail shapes match the
/// product's pre-send guard (CorrectionTriagePreSendGuard). Used by dataset validity tests and by graders.
/// </summary>
public static class EvalPii
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Regex EmailShape = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex PhoneShape = new(
        @"(?<!\d)(?:\+?48[\s-]?)?\d{3}[\s-]?\d{3}[\s-]?\d{3}(?!\d)",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex PeselShape = new(
        @"(?<!\d)\d{11}(?!\d)",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex PostalShape = new(
        @"(?<!\d)\d{2}-\d{3}(?!\d)",
        RegexOptions.CultureInvariant,
        Timeout);

    public static bool LooksLikePii(string text) =>
        EmailShape.IsMatch(text)
        || PhoneShape.IsMatch(text)
        || PeselShape.IsMatch(text)
        || PostalShape.IsMatch(text);
}
