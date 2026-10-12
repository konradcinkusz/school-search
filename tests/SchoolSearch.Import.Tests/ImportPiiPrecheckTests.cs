using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Tests;

/// <summary>
/// The bare-list screen in <see cref="ImportPiiPrecheck"/>. The lines are built from synthetic, letter-only tokens:
/// AGENTS.md forbids pupil names in fixtures, so no real or realistic name appears here.
/// </summary>
public sealed class ImportPiiPrecheckTests
{
    private const string ListHit = "list of two-word capitalised names (possible pupil list)";

    private static readonly string[] SyntheticPairs =
    [
        "Aaaa Bbbb",
        "Cccc Dddd",
        "Eeee Ffff",
        "Gggg Hhhh",
        "Iiii Jjjj",
        "Kkkk Llll",
    ];

    [Fact]
    public void Five_bare_two_word_name_lines_are_flagged()
    {
        var text = string.Join("\n", SyntheticPairs.Take(5));

        var hits = ImportPiiPrecheck.Find(text);

        Assert.Contains(ListHit, hits);
    }

    [Fact]
    public void Five_numbered_name_lines_with_crlf_are_flagged()
    {
        var lines = SyntheticPairs.Take(5)
            .Select((pair, i) => (i % 2 == 0 ? $"{i + 1}. {pair}" : $"{i + 1}) {pair}"));

        var hits = ImportPiiPrecheck.Find(string.Join("\r\n", lines));

        Assert.Contains(ListHit, hits);
    }

    [Fact]
    public void Four_name_lines_are_not_flagged()
    {
        var text = string.Join("\n", SyntheticPairs.Take(4));

        var hits = ImportPiiPrecheck.Find(text);

        Assert.Empty(hits);
    }

    [Fact]
    public void Only_the_pattern_name_is_reported_never_the_matched_text()
    {
        var hits = ImportPiiPrecheck.Find(string.Join("\n", SyntheticPairs.Take(5)));

        var hit = Assert.Single(hits);
        Assert.Equal(ListHit, hit);
        Assert.DoesNotContain("Aaaa", hit, StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_street_resolution_sample_is_not_flagged()
    {
        const string sample =
            "Uchwała nr 12/2026 Rady Miejskiej\n" +
            "Szkoła Podstawowa nr 5 obejmuje ulicę Kwiatową, numery parzyste 2-18.\n" +
            "ul. Wiśniowa\n" +
            "ul. Lipowa\n" +
            "ul. Zielona\n" +
            "ul. Krótka\n" +
            "ul. Długa\n" +
            "Pozostałe ulice należą do Szkoły Podstawowej nr 7.";

        Assert.Empty(ImportPiiPrecheck.Find(sample));
    }
}
