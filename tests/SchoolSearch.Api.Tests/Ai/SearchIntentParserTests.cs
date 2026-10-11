using SchoolSearch.Api.Ai;
using Xunit;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>The parser is the trust boundary for model output: only validated values may become filters.</summary>
public sealed class SearchIntentParserTests
{
    private const string Question = "Szkoła w gminie 1465011 z matematyką, tylko publiczna, do 5 km";

    [Fact]
    public void A_fenced_or_prefixed_object_is_read()
    {
        var result = SearchIntentParser.Parse(
            "Oto wynik:\n```json\n{\"gmina\":\"1465011\",\"q\":\"matematyka\",\"publicOnly\":true,\"maxDistanceKm\":5,\"type\":\"school\",\"unsupportedKinds\":[]}\n```",
            Question);

        Assert.NotNull(result);
        Assert.Equal("1465011", result.Filters.Gmina);
        Assert.Equal("matematyka", result.Filters.Q);
        Assert.Equal(true, result.Filters.PublicOnly);
        Assert.Equal(5d, result.Filters.MaxDistanceKm);
        Assert.Equal("school", result.Filters.Type);
        Assert.Empty(result.Filters.Notes);
        Assert.Empty(result.Unsupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Brak filtrów.")]
    [InlineData("{\"gmina\": ")]
    [InlineData("{\"gmina\":\"1465011\",}")]
    public void Text_that_is_not_a_single_object_is_rejected_whole(string modelText)
    {
        Assert.Null(SearchIntentParser.Parse(modelText, Question));
    }

    [Theory]
    [InlineData("najlepsza szkoła")]
    [InlineData("Ranking szkół")]
    [InlineData("polecane")]
    public void Quality_words_in_q_are_routed_to_unsupported_and_q_is_dropped(string query)
    {
        var result = SearchIntentParser.Parse(
            "{\"q\":\"" + query + "\",\"unsupportedKinds\":[]}",
            "Szkoła w okolicy");

        Assert.NotNull(result);
        Assert.Null(result.Filters.Q);
        Assert.Contains(result.Unsupported, text => text.Contains("nie układa szkół według jakości", StringComparison.Ordinal));
    }

    [Fact]
    public void Sort_request_in_q_is_routed_to_unsupported()
    {
        var result = SearchIntentParser.Parse("{\"q\":\"sortuj malejąco\"}", "Szkoła w okolicy");

        Assert.NotNull(result);
        Assert.Null(result.Filters.Q);
        Assert.Contains(result.Unsupported, text => text.Contains("Sortowanie według wyniku", StringComparison.Ordinal));
    }

    [Fact]
    public void Ranking_words_in_the_question_are_unsupported_even_when_the_model_is_silent()
    {
        var result = SearchIntentParser.Parse("{}", "Która szkoła jest najlepsza?");

        Assert.NotNull(result);
        Assert.Contains(result.Unsupported, text => text.Contains("nie układa szkół według jakości", StringComparison.Ordinal));
        Assert.Null(result.Filters.Q);
        Assert.Null(result.Filters.Gmina);
    }

    [Fact]
    public void Model_unsupported_kind_is_mapped_to_the_fixed_polish_sentence()
    {
        var result = SearchIntentParser.Parse("{\"unsupportedKinds\":[\"quality_or_ranking\",\"sort_order\",\"invented_kind\"]}", "Szkoła");

        Assert.NotNull(result);
        Assert.Equal(
            new[] { SearchIntentParser.QualityUnsupported, SearchIntentParser.SortUnsupported },
            result.Unsupported);
    }

    [Fact]
    public void Gmina_is_accepted_only_when_the_question_states_the_same_seven_digits()
    {
        var stated = SearchIntentParser.Parse("{\"gmina\":\"1465011\"}", "Szkoła w 1465011");
        var invented = SearchIntentParser.Parse("{\"gmina\":\"1465011\"}", "Szkoła w 14650110");
        var wrongLength = SearchIntentParser.Parse("{\"gmina\":\"146501\"}", "Szkoła w 146501");

        Assert.NotNull(stated);
        Assert.Equal("1465011", stated.Filters.Gmina);
        Assert.NotNull(invented);
        Assert.Null(invented.Filters.Gmina);
        Assert.NotNull(wrongLength);
        Assert.Null(wrongLength.Filters.Gmina);
    }

    [Theory]
    [InlineData("{\"gmina\":1465011}")]
    [InlineData("{\"publicOnly\":\"yes\"}")]
    [InlineData("{\"maxDistanceKm\":\"5\"}")]
    [InlineData("{\"maxDistanceKm\":-3}")]
    [InlineData("{\"maxDistanceKm\":0}")]
    [InlineData("{\"type\":[\"school\"]}")]
    [InlineData("{\"q\":42}")]
    public void A_field_of_the_wrong_type_is_dropped_with_a_note_and_never_guessed(string modelJson)
    {
        var result = SearchIntentParser.Parse(modelJson, "Szkoła w Krakowie 5 km");

        Assert.NotNull(result);
        Assert.Null(result.Filters.Gmina);
        Assert.Null(result.Filters.PublicOnly);
        Assert.Null(result.Filters.MaxDistanceKm);
        Assert.Null(result.Filters.Type);
        Assert.Null(result.Filters.Q);
        Assert.NotEmpty(result.Filters.Notes);
    }

    [Fact]
    public void Distance_is_clamped_to_fifty_kilometres()
    {
        var result = SearchIntentParser.Parse("{\"maxDistanceKm\":120.5}", "Szkoła w promieniu 120 km");

        Assert.NotNull(result);
        Assert.Equal(SearchIntentParser.MaxDistanceKm, result.Filters.MaxDistanceKm);
        Assert.Contains(result.Filters.Notes, note => note.Contains("ograniczono do 50 km", StringComparison.Ordinal));
    }

    [Fact]
    public void Public_only_false_means_no_restriction_and_is_not_set()
    {
        var result = SearchIntentParser.Parse("{\"publicOnly\":false}", "Każda szkoła");

        Assert.NotNull(result);
        Assert.Null(result.Filters.PublicOnly);
        Assert.Empty(result.Filters.Notes);
    }

    [Theory]
    [InlineData("School")]
    [InlineData(" preschool ")]
    public void Type_is_case_and_space_insensitive_within_the_allowlist(string type)
    {
        var result = SearchIntentParser.Parse("{\"type\":\"" + type + "\"}", "Przedszkole");

        Assert.NotNull(result);
        Assert.Equal(type.Trim().ToLowerInvariant(), result.Filters.Type);
    }

    [Fact]
    public void Query_with_angle_brackets_or_personal_data_is_dropped_with_a_note()
    {
        var markup = SearchIntentParser.Parse("{\"q\":\"<b>matematyka</b>\"}", "Szkoła");
        var email = SearchIntentParser.Parse("{\"q\":\"rodzic@example.com\"}", "Szkoła");

        Assert.NotNull(markup);
        Assert.Null(markup.Filters.Q);
        Assert.NotNull(email);
        Assert.Null(email.Filters.Q);
    }

    [Fact]
    public void Extra_fields_the_schema_does_not_declare_are_ignored()
    {
        var result = SearchIntentParser.Parse("{\"sortBy\":\"score\",\"rank\":1,\"type\":\"school\"}", "Szkoła");

        Assert.NotNull(result);
        Assert.Equal("school", result.Filters.Type);
        Assert.Empty(result.Unsupported);
    }
}
