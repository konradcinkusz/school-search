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

    [Theory]
    [InlineData("school", "school")]
    [InlineData("Preschool", "preschool")]
    [InlineData(" all ", "all")]
    public void Kind_is_accepted_within_the_allowlist(string kind, string expected)
    {
        var result = SearchIntentParser.Parse("{\"kind\":\"" + kind + "\"}", "Przedszkole");

        Assert.NotNull(result);
        Assert.Equal(expected, result.Filters.Kind);
        Assert.Empty(result.Unsupported);
    }

    [Theory]
    [InlineData("{\"kind\":\"secondary\"}")]
    [InlineData("{\"kind\":\"\"}")]
    [InlineData("{\"kind\":1}")]
    [InlineData("{\"kind\":[\"school\"]}")]
    public void Kind_outside_the_allowlist_is_unsupported_and_never_guessed(string modelJson)
    {
        var result = SearchIntentParser.Parse(modelJson, "Liceum");

        Assert.NotNull(result);
        Assert.Null(result.Filters.Kind);
        Assert.Contains(SearchIntentParser.KindUnsupported, result.Unsupported);
    }

    [Fact]
    public void Nursery_is_unsupported_from_kind_unsupported_kinds_or_the_question()
    {
        var fromKind = SearchIntentParser.Parse("{\"kind\":\"nursery\"}", "Szukam opieki");
        var fromList = SearchIntentParser.Parse("{\"unsupportedKinds\":[\"nursery\"]}", "Szukam opieki");
        var fromQuestion = SearchIntentParser.Parse("{\"kind\":\"preschool\"}", "Żłobek lub przedszkole");

        Assert.NotNull(fromKind);
        Assert.Null(fromKind.Filters.Kind);
        Assert.Contains(SearchIntentParser.NurseryUnsupported, fromKind.Unsupported);
        Assert.NotNull(fromList);
        Assert.Contains(SearchIntentParser.NurseryUnsupported, fromList.Unsupported);
        Assert.NotNull(fromQuestion);
        Assert.Contains(SearchIntentParser.NurseryUnsupported, fromQuestion.Unsupported);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1500", 1500)]
    [InlineData("20000", 20000)]
    public void Max_tuition_accepts_integers_from_zero_to_twenty_thousand(string json, int expected)
    {
        var result = SearchIntentParser.Parse("{\"maxTuitionPln\":" + json + "}", "Przedszkole do 1500 zł");

        Assert.NotNull(result);
        Assert.Equal(expected, result.Filters.MaxTuitionPln);
        Assert.Empty(result.Unsupported);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("20001")]
    [InlineData("1500.5")]
    [InlineData("\"1500\"")]
    [InlineData("true")]
    [InlineData("99999999999")]
    public void Max_tuition_outside_the_range_or_of_the_wrong_type_is_unsupported(string json)
    {
        var result = SearchIntentParser.Parse("{\"maxTuitionPln\":" + json + "}", "Przedszkole");

        Assert.NotNull(result);
        Assert.Null(result.Filters.MaxTuitionPln);
        Assert.Contains(SearchIntentParser.TuitionUnsupported, result.Unsupported);
    }

    [Fact]
    public void Has_free_places_true_is_set_false_is_unset_and_other_types_are_unsupported()
    {
        var yes = SearchIntentParser.Parse("{\"hasFreePlaces\":true}", "Z wolnymi miejscami");
        var no = SearchIntentParser.Parse("{\"hasFreePlaces\":false}", "Przedszkole");
        var text = SearchIntentParser.Parse("{\"hasFreePlaces\":\"tak\"}", "Przedszkole");

        Assert.NotNull(yes);
        Assert.Equal(true, yes.Filters.HasFreePlaces);
        Assert.NotNull(no);
        Assert.Null(no.Filters.HasFreePlaces);
        Assert.Empty(no.Unsupported);
        Assert.NotNull(text);
        Assert.Null(text.Filters.HasFreePlaces);
        Assert.Contains(SearchIntentParser.FreePlacesUnsupported, text.Unsupported);
    }

    [Theory]
    [InlineData("Plac Wolności, Poznań")]
    [InlineData("  Stary   Rynek,  Poznań ")]
    [InlineData("ul. Długa 15, Gdańsk")]
    public void Place_is_trimmed_and_collapsed_when_it_is_a_plain_place_phrase(string place)
    {
        var result = SearchIntentParser.Parse("{\"place\":\"" + place + "\"}", "Blisko Placu Wolności");

        Assert.NotNull(result);
        Assert.Equal(SearchIntentGuard.Collapse(place), result.Filters.Place);
        Assert.Empty(result.Unsupported);
    }

    [Theory]
    [InlineData("52.2297, 21.0122")]
    [InlineData("52,2297 21,0122")]
    [InlineData("lat 52.1 lon 21")]
    [InlineData("latitude 52 longitude 21")]
    [InlineData("52.2297")]
    [InlineData("jan.kowalski@example.com")]
    [InlineData("600 123 456")]
    [InlineData("44051401359")]
    [InlineData("12345678")]
    [InlineData("30-001 Kraków")]
    [InlineData("<b>Poznań</b>")]
    [InlineData("Jan Kowalski, Anna Nowak")]
    [InlineData("Jan Kowalski i Anna Nowak")]
    [InlineData("najlepsza okolica")]
    [InlineData("12345")]
    public void Place_with_coordinates_personal_data_markup_names_or_ranking_is_rejected_whole(string place)
    {
        var result = SearchIntentParser.Parse("{\"place\":\"" + place + "\"}", "Szkoła w okolicy");

        Assert.NotNull(result);
        Assert.Null(result.Filters.Place);
        Assert.NotEmpty(result.Unsupported);
    }

    [Fact]
    public void Place_longer_than_120_characters_or_of_the_wrong_type_is_rejected()
    {
        var tooLong = SearchIntentParser.Parse("{\"place\":\"" + new string('a', 121) + "\"}", "Szkoła");
        var atLimit = SearchIntentParser.Parse("{\"place\":\"" + new string('a', 120) + "\"}", "Szkoła");
        var number = SearchIntentParser.Parse("{\"place\":52}", "Szkoła");

        Assert.NotNull(tooLong);
        Assert.Null(tooLong.Filters.Place);
        Assert.Contains(SearchIntentParser.PlaceUnsupported, tooLong.Unsupported);
        Assert.NotNull(atLimit);
        Assert.Equal(120, atLimit.Filters.Place?.Length);
        Assert.NotNull(number);
        Assert.Null(number.Filters.Place);
        Assert.Contains(SearchIntentParser.PlaceUnsupported, number.Unsupported);
    }

    [Fact]
    public void New_fields_are_null_when_the_model_does_not_set_them()
    {
        var result = SearchIntentParser.Parse("{}", "Szkoła");

        Assert.NotNull(result);
        Assert.Null(result.Filters.Kind);
        Assert.Null(result.Filters.MaxTuitionPln);
        Assert.Null(result.Filters.HasFreePlaces);
        Assert.Null(result.Filters.Place);
    }

    [Fact]
    public void Combined_new_fields_are_all_read()
    {
        var result = SearchIntentParser.Parse(
            "{\"kind\":\"preschool\",\"maxTuitionPln\":1500,\"hasFreePlaces\":true,\"place\":\"Plac Wolności, Poznań\",\"unsupportedKinds\":[]}",
            "Przedszkole do 1500 zł z wolnymi miejscami blisko Placu Wolności w Poznaniu");

        Assert.NotNull(result);
        Assert.Equal("preschool", result.Filters.Kind);
        Assert.Equal(1500, result.Filters.MaxTuitionPln);
        Assert.Equal(true, result.Filters.HasFreePlaces);
        Assert.Equal("Plac Wolności, Poznań", result.Filters.Place);
        Assert.Empty(result.Unsupported);
        Assert.Empty(result.Filters.Notes);
    }
}
