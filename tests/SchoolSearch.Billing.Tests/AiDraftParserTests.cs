using SchoolSearch.Billing.Ai;
using Xunit;

namespace SchoolSearch.Billing.Tests;

/// <summary>The model answer and the owner's facts are untrusted: only one clean object within the limits is read.</summary>
public sealed class AiDraftParserTests
{
    [Theory]
    [InlineData("{\"headline\":\"Szkoła z klasami sportowymi\",\"description\":\"Zajęcia w klasach sportowych.\"}")]
    [InlineData("```json\n{\"headline\":\"Szkoła z klasami sportowymi\",\"description\":\"Zajęcia w klasach sportowych.\"}\n```")]
    [InlineData("Oto propozycja: {\"headline\":\"Szkoła z klasami sportowymi\",\"description\":\"Zajęcia w klasach sportowych.\"} dziękuję")]
    public void Valid_object_is_read(string modelText)
    {
        var candidate = DraftParser.Parse(modelText);

        Assert.NotNull(candidate);
        Assert.Equal("Szkoła z klasami sportowymi", candidate!.Headline);
        Assert.Equal("Zajęcia w klasach sportowych.", candidate.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"headline\":\"Szkoła\"}")]
    [InlineData("{\"headline\":5,\"description\":\"Opis\"}")]
    [InlineData("{\"headline\":\"\",\"description\":\"Opis\"}")]
    [InlineData("{\"headline\":\"Nagłówek\",\"description\":\"<b>Opis</b>\"}")]
    [InlineData("{\"headline\":\"<script>\",\"description\":\"Opis\"}")]
    public void Anything_else_is_not_a_draft(string? modelText)
    {
        Assert.Null(DraftParser.Parse(modelText));
    }

    [Fact]
    public void Headline_over_the_profile_limit_is_refused_not_truncated()
    {
        var headline = new string('a', DraftParser.MaxHeadlineLength + 1);

        Assert.Null(DraftParser.Parse("{\"headline\":\"" + headline + "\",\"description\":\"Opis\"}"));
    }

    [Fact]
    public void Description_over_the_profile_limit_is_refused_not_truncated()
    {
        var description = new string('a', DraftParser.MaxDescriptionLength + 1);

        Assert.Null(DraftParser.Parse("{\"headline\":\"Nagłówek\",\"description\":\"" + description + "\"}"));
    }

    [Fact]
    public void Control_characters_become_single_spaces()
    {
        var candidate = DraftParser.Parse("{\"headline\":\"Szkoła\\n z   klasami\",\"description\":\"Opis\\tpierwszy\\r\\ndrugi\"}");

        Assert.NotNull(candidate);
        Assert.Equal("Szkoła z klasami", candidate!.Headline);
        Assert.Equal("Opis pierwszy drugi", candidate.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(DraftInputValidator.MaxBullets + 1)]
    public void Bullet_count_outside_the_limit_is_refused(int count)
    {
        var bullets = Enumerable.Repeat("Klasy sportowe", count).ToArray();

        var (result, error) = DraftInputValidator.Validate(bullets);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Missing_bullets_are_refused()
    {
        var (result, error) = DraftInputValidator.Validate(null);

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("Klasy\nsportowe")]
    [InlineData("Klasy\tsportowe")]
    public void Empty_or_multi_line_bullet_is_refused(string bullet)
    {
        var (result, error) = DraftInputValidator.Validate(new[] { "Boisko", bullet });

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Bullet_over_the_length_limit_is_refused()
    {
        var (result, error) = DraftInputValidator.Validate(new[] { new string('a', DraftInputValidator.MaxBulletLength + 1) });

        Assert.Null(result);
        Assert.NotNull(error);
    }

    [Fact]
    public void Valid_bullets_are_trimmed_and_kept_in_order()
    {
        var (result, error) = DraftInputValidator.Validate(new[] { "  Boisko szkolne ", "Klasy sportowe od 4 klasy" });

        Assert.Null(error);
        Assert.Equal(new[] { "Boisko szkolne", "Klasy sportowe od 4 klasy" }, result);
    }
}
