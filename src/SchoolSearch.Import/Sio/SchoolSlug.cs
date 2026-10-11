using System;
using System.Globalization;
using System.Text;

namespace SchoolSearch.Import;

/// <summary>
/// Deterministic, URL-safe school slugs. The slug is <c>{folded name}-{idPodmiot}</c>. The id suffix makes it
/// unique, and the same input always gives the same output.
/// </summary>
/// <remarks>
/// The loader calls <see cref="Create"/> only when a school is first inserted. On every later import the stored
/// slug is kept, even if the name changes, so public links never break.
/// </remarks>
public static class SchoolSlug
{
    private const int MaxNameLength = 80;
    private const string Fallback = "szkola";

    public static string Create(string name, long idPodmiot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(idPodmiot);

        var text = Fold(name);
        if (text.Length > MaxNameLength)
        {
            text = text[..MaxNameLength].TrimEnd('-');
        }

        if (text.Length == 0)
        {
            text = Fallback;
        }

        return string.Concat(text, "-", idPodmiot.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Folds Polish (and other Latin) diacritics to ASCII, lowercases, and collapses every other run of
    /// characters into a single '-'. Leading and trailing '-' are removed.
    /// </summary>
    public static string Fold(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var builder = new StringBuilder(input.Length);
        foreach (var ch in input.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(ch);
            if (lower == 'ł')
            {
                // U+0142 has no canonical decomposition, so it is folded explicitly.
                builder.Append('l');
            }
            else if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[builder.Length - 1] != '-')
            {
                builder.Append('-');
            }
        }

        if (builder.Length > 0 && builder[builder.Length - 1] == '-')
        {
            builder.Length--;
        }

        return builder.ToString();
    }
}
