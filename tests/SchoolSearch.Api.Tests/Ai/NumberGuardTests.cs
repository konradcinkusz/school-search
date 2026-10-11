using SchoolSearch.Api.Ai;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

public sealed class NumberGuardTests
{
    private const string Facts = "{\"score\":{\"score\":-3.75,\"cohortSize\":10,\"examYear\":2025},\"methodologyVersion\":\"1.0.0\"}";

    [Theory]
    [InlineData("Wynik to -3,75 punktu.")]
    [InlineData("W kohorcie 10 uczniów, rok 2025.")]
    [InlineData("Wersja metodologii 1.0.0.")]
    public void Text_whose_digit_runs_all_appear_in_the_facts_is_grounded(string text)
    {
        Assert.True(NumberGuard.IsGrounded(text, Facts));
    }

    [Theory]
    [InlineData("Szkoła osiągnęła 98765 punktów.")]
    [InlineData("Wynik to 3,755 punktu.")]
    [InlineData("Wynik to 375 punktów.")]
    [InlineData("Liczba uczniów: 1 000.")]
    public void Text_with_a_number_the_facts_do_not_contain_is_not_grounded(string text)
    {
        Assert.False(NumberGuard.IsGrounded(text, Facts));
    }

    [Fact]
    public void Text_without_digits_is_grounded()
    {
        Assert.True(NumberGuard.IsGrounded("Wynik nie jest oficjalnym rankingiem.", Facts));
    }
}
