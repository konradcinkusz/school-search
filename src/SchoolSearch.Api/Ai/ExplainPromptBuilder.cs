using System.Text.Encodings.Web;
using System.Text.Json;

namespace SchoolSearch.Api.Ai;

/// <summary>A fully built prompt. <see cref="FactsJson"/> is the exact facts payload the output must be grounded in.</summary>
public sealed record ExplainPrompt(string System, string User, string FactsJson);

/// <summary>
/// Builds the Polish system rules and the deterministic facts payload. Same facts always produce the same bytes,
/// so the grounding check and the prompt agree.
/// </summary>
public static class ExplainPromptBuilder
{
    /// <summary>The closing sentence every explanation must end with (AI-FEATURES feature 1, METRIC-ETHICS).</summary>
    public const string Disclaimer = "Wynik nie jest oficjalnym rankingiem i nie ocenia nauczycieli ani uczniów.";

    private static readonly string[] SystemRules =
    [
        "Jesteś asystentem, który wyjaśnia publiczny wynik szkoły podstawowej z egzaminu ósmoklasisty prostym językiem polskim.",
        "Zasady, których nie wolno złamać:",
        "1. Wyjaśnij, co znaczy wynik: punkty procentowe względem średniej krajowej w tych samych przedmiotach (0 oznacza średnią krajową), liczbę uczniów w kohorcie oraz pokrycie. Przedział pokazuje tylko, jak bardzo wynik mógłby się zmienić z samej losowości wyników uczniów; nie obejmuje błędów danych ani składu uczniów.",
        "2. Używaj wyłącznie liczb i faktów z dostarczonych danych JSON. Nie dodawaj żadnej liczby spoza nich, nawet przybliżonej.",
        "3. Jeśli scoreStatus to insufficient_cohort, wyjaśnij, że liczba zdających jest poniżej progu publikacji z danych, dlatego wynik nie jest podawany. Nie podawaj wtedy żadnej wartości wyniku. Jeśli scoreStatus to no_exam_data, napisz, że brak wyników egzaminu tej szkoły. Jeśli scoreStatus to no_baseline, napisz, że brak porównania krajowego, więc wynik nie może być obliczony.",
        "4. Nie porównuj szkoły z żadną inną szkołą. Nie twórz rankingu i nie używaj słów takich jak najlepsza, najgorsza, lepsza czy gorsza.",
        "5. Nie podawaj przyczyn wyniku. Nie pisz o jakości nauczania, nauczycielach, uczniach, rodzinach ani ich pochodzeniu. Wynik opisuje szkołę, a nie ludzi. Zaznacz, że wynik nie koryguje tego, jacy uczniowie szkoła przyjmuje, więc nie mówi wyłącznie o pracy szkoły.",
        "6. Wynik oparty na małej kohorcie lub niskim pokryciu jest wczesnym, niepewnym sygnałem, a nie dowodem, że szkoła jest dobra lub zła. Niskie pokrycie przesuwa wynik w stronę zera, niezależnie od tego, jak wypadli zdający.",
        "7. Nie używaj słów pewny, gwarantowany ani na pewno.",
        "8. Odpowiedź to zwykły tekst bez nagłówków, list i znaczników, najwyżej 140 słów.",
        "9. Zakończ odpowiedź dokładnie tym zdaniem: " + Disclaimer,
    ];

    // Unicode is written as-is (not escaped), so the prompt stays readable; the same string is used for grounding.
    private static readonly JsonSerializerOptions PromptJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly string SystemPrompt = string.Join("\n", SystemRules);

    public static ExplainPrompt Build(ExplainFacts facts)
    {
        var json = JsonSerializer.Serialize(facts, PromptJsonOptions);
        var user = "Dane szkoły (JSON). To jedyne źródło liczb i faktów:\n"
            + json
            + "\n\nNapisz wyjaśnienie zgodnie z zasadami.";

        return new ExplainPrompt(SystemPrompt, user, json);
    }

    /// <summary>
    /// Final text as shown to the reader: trimmed, and the mandated closing sentence is appended when the model omitted it.
    /// Empty text stays empty so the caller can reject it.
    /// </summary>
    public static string Finish(string modelText)
    {
        var text = modelText.Trim();
        if (text.Length == 0 || text.Contains(Disclaimer, StringComparison.Ordinal))
        {
            return text;
        }

        return text + " " + Disclaimer;
    }
}
