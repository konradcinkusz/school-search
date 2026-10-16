using SchoolSearch.Api.Ai;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

public sealed class SearchIntentGuardTests
{
    [Theory]
    [InlineData("Szkoła w gminie 1465011 z matematyką")]
    [InlineData("Przedszkole do 5 km, tylko publiczne, 2 dzieci")]
    [InlineData("Szkoła w Krakowie, rok 2025")]
    public void Ordinary_questions_pass_the_guard(string question)
    {
        Assert.Null(SearchIntentGuard.Validate(question));
    }

    [Theory]
    [InlineData("rodzic@example.com")]
    [InlineData("Zadzwoń 600 123 456")]
    [InlineData("+48 600-123-456")]
    [InlineData("PESEL 44051401359")]
    [InlineData("Kod 30-001")]
    public void Obvious_personal_data_is_refused(string question)
    {
        Assert.Equal(SearchIntentGuard.PersonalDataMessage, SearchIntentGuard.Validate(question));
    }

    [Fact]
    public void Empty_and_overlong_questions_are_refused_with_polish_messages()
    {
        Assert.Equal(SearchIntentGuard.EmptyMessage, SearchIntentGuard.Validate("  "));
        Assert.Equal(SearchIntentGuard.EmptyMessage, SearchIntentGuard.Validate(null));
        Assert.Equal(SearchIntentGuard.TooLongMessage, SearchIntentGuard.Validate(new string('a', 301)));
        Assert.Null(SearchIntentGuard.Validate(new string('a', 300)));
    }

    [Fact]
    public void Normalize_lowercases_trims_and_collapses_whitespace()
    {
        Assert.Equal("szkoła w krakowie", SearchIntentGuard.Normalize("  Szkoła   W\tKrakowie "));
    }

    [Theory]
    [InlineData("Najlepsza szkoła")]
    [InlineData("która jest najgorsza")]
    [InlineData("lepsza niż inne")]
    [InlineData("ranking szkół")]
    [InlineData("best school")]
    public void Ranking_words_are_detected(string text)
    {
        Assert.True(SearchIntentGuard.MentionsRanking(text));
    }

    [Theory]
    [InlineData("posortuj po wyniku")]
    [InlineData("rosnąco")]
    [InlineData("malejąco")]
    [InlineData("najtańsze przedszkola")]
    [InlineData("NAJTAŃSZY czesne")]
    [InlineData("najtanszych szkół")]
    [InlineData("najdroższe")]
    [InlineData("od najtańszego")]
    [InlineData("posortuj po cenie")]
    public void Sorting_words_are_detected(string text)
    {
        Assert.True(SearchIntentGuard.MentionsSorting(text));
    }

    [Theory]
    [InlineData("szkoły dla dzieci z Ukrainy")]
    [InlineData("Narodowość uczniów")]
    [InlineData("cudzoziemcy w szkole")]
    [InlineData("OBCOKRAJOWCY")]
    [InlineData("pochodzenie uczniów")]
    public void Nationality_words_are_detected(string text)
    {
        Assert.True(SearchIntentGuard.MentionsNationality(text));
    }

    [Fact]
    public void Plain_filter_words_are_not_treated_as_ranking()
    {
        Assert.False(SearchIntentGuard.MentionsRanking("szkoła podstawowa w gminie 1465011 do 5 km"));
        Assert.False(SearchIntentGuard.MentionsSorting("szkoła podstawowa w gminie 1465011 do 5 km"));
        Assert.False(SearchIntentGuard.MentionsNationality("szkoła podstawowa w gminie 1465011 do 5 km"));
    }
}
