using SchoolSearch.Billing.Claims;

namespace SchoolSearch.Billing.Tests;

public sealed class ClaimLinterTests
{
    [Theory]
    [InlineData("Najlepsza szkoła w mieście", "Najlepsza")]
    [InlineData("Jesteśmy numer 1 w regionie", "numer 1")]
    [InlineData("Szkoła w TOP 10 rankingu", "TOP 10")]
    [InlineData("Wysoki wynik egzaminu ósmoklasisty", "wynik egzaminu")]
    [InlineData("Lepsza niż inne szkoły", "Lepsza niż")]
    [InlineData("Gwarantujemy sukces", "Gwarantujemy")]
    [InlineData("Osiągnij 100% satysfakcji", "100%")]
    [InlineData("Szkoła certyfikowana", "certyfikowana")]
    [InlineData("Profil zweryfikowany przez nas", "zweryfikowany")]
    [InlineData("The best school", "best")]
    [InlineData("We guarantee results", "guarantee")]
    [InlineData("Oficjalny ranking gazety", "Oficjalny ranking")]
    [InlineData("Rankingu nie ma", "Rankingu")]
    [InlineData("Szkoła #1 w gminie", "#1")]
    [InlineData("Nr 1 w powiecie", "Nr 1")]
    [InlineData("We are verified by nobody", "verified")]
    public void Forbidden_terms_are_reported_exactly_as_written(string text, string term)
    {
        Assert.Equal(term, ClaimLinter.Check(text));
    }

    [Theory]
    [InlineData("Szkoła z klasami sportowymi i angielskim.")]
    [InlineData("Nauczyciele prowadzą zajęcia w małych grupach.")]
    [InlineData("Adres: numer 10, kod 00-001")]
    [InlineData("")]
    public void Ordinary_text_passes(string text)
    {
        Assert.Null(ClaimLinter.Check(text));
    }
}
