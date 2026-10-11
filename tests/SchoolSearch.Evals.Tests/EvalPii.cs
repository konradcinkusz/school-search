using System.Text.RegularExpressions;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Shape checks for personal data in dataset text: an e-mail address, a Polish telephone number (9 digits with optional
/// +48 and separators), an 11-digit PESEL-like run and a NN-NNN postal code. The first three match the product's
/// pre-send guard (CorrectionTriagePreSendGuard); the postal code is only a dataset-hygiene shape.
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

    /// <summary>E-mail, telephone or PESEL shape: the same contact shapes the product's pre-send guard refuses.</summary>
    public static bool LooksLikeContact(string text) =>
        EmailShape.IsMatch(text)
        || PhoneShape.IsMatch(text)
        || PeselShape.IsMatch(text);

    /// <summary>Contact shapes plus a NN-NNN postal code. Postal codes are not refused by the product, so graders use <see cref="LooksLikeContact"/>.</summary>
    public static bool LooksLikePii(string text) => LooksLikeContact(text) || PostalShape.IsMatch(text);
}
