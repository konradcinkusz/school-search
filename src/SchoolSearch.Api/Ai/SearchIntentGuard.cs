using System.Text.RegularExpressions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Deterministic checks that run before anything is sent to a model and after the answer comes back: the question's
/// shape, obvious personal data (never sent), and the words that mean "ranking" or "sorting" (never turned into filters).
/// </summary>
public static partial class SearchIntentGuard
{
    public const int MaxQuestionLength = 300;
    public const string EmptyMessage = "Opisz, czego szukasz.";
    public const string TooLongMessage = "Pytanie może mieć najwyżej 300 znaków.";
    public const string NationalityMessage = "Nie wyszukujemy szkół według narodowości ani pochodzenia uczniów.";
    public const string PersonalDataMessage = "Nie wpisuj adresu, numeru telefonu ani danych dziecka.";

    [GeneratedRegex(@"[\p{L}0-9._%+-]+@[\p{L}0-9.-]+\.\p{L}{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<!\d)\d{11}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PeselPattern();

    [GeneratedRegex(@"(?<!\d)\d{2}-\d{3}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PostalCodePattern();

    [GeneratedRegex(@"(?<!\d)(?:\+?\d{2}[\s-]?)?(?:\d{3}[\s-]?){2}\d{3}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex("najlepsz|najgorsz|najlepiej|najgorzej|najsłabsz|najmocniejsz|najwyżej oceni|najniżej oceni|najwyższ|najniższ|lepsz|gorsz|ranking|rankin|polecan|\\bbest\\b|\\bworst\\b|\\btop\\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RankingWords();

    [GeneratedRegex("sortuj|sortowa|rosnąc|malejąc|ascending|descending|najtańsz|najtansz|najtani|najdroższ|najdrozsz|najdrożej|najdrozej|po cenie|według ceny|wedlug ceny", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SortingWords();

    [GeneratedRegex("narodowo|pochodzeni|cudzoziem|obcokrajow|ukrain|ukraiń", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NationalityWords();

    /// <summary>Null when the question may be sent; otherwise the Polish message shown to the parent.</summary>
    public static string? Validate(string? question)
    {
        var text = question?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return EmptyMessage;
        }

        if (text.Length > MaxQuestionLength)
        {
            return TooLongMessage;
        }

        return ContainsPersonalData(text) ? PersonalDataMessage : null;
    }

    /// <summary>True for e-mail addresses, phone numbers, PESEL-like digit runs and postal codes.</summary>
    public static bool ContainsPersonalData(string text) =>
        EmailPattern().IsMatch(text)
        || PeselPattern().IsMatch(text)
        || PostalCodePattern().IsMatch(text)
        || PhonePattern().IsMatch(text);

    /// <summary>Lowercase, trimmed, whitespace collapsed. Used for the cache key only.</summary>
    public static string Normalize(string text) => Collapse(text).ToLowerInvariant();

    public static bool MentionsRanking(string text) => RankingWords().IsMatch(text);

    public static bool MentionsSorting(string text) => SortingWords().IsMatch(text);

    public static bool MentionsNationality(string text) => NationalityWords().IsMatch(text);

    /// <summary>Trims and collapses runs of whitespace to a single space.</summary>
    public static string Collapse(string text) =>
        string.Join(" ", text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
}
