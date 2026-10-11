using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Preschools.Extract;

/// <summary>
/// Operator-written prompt for the preschool tuition-table extractor. The document is only ever in the user turn,
/// wrapped by <see cref="UntrustedText.Wrap"/>, so its angle brackets cannot forge the delimiter.
/// </summary>
public static class PreschoolExtractPrompt
{
    /// <summary>Room for the JSON rows of one chunk of at most 6000 characters.</summary>
    public const int MaxOutputTokens = 3000;

    public const string System =
        "You extract preschool (przedszkole) tuition facts from one saved tuition table. " +
        UntrustedText.DataOnlyRule + " " +
        "Output ONLY one JSON object of the form {\"rows\":[...]}, with no prose and no markdown. " +
        "Each row has exactly these keys: " +
        "\"nameAsWritten\" (the preschool name exactly as written in the table); " +
        "\"tuitionMin\" and \"tuitionMax\" (monthly tuition in PLN, copied as written, for example \"450,50\", or null); " +
        "\"meal\" (monthly meal fee in PLN as written, or null); " +
        "\"opens\" and \"closes\" (\"HH:mm\" or null); " +
        "\"freePlaces\" (a whole number or null); " +
        "\"freePlacesAsOf\" (\"yyyy-MM-dd\" or null); " +
        "\"quote\" (the exact text of the table that states these values, at most 300 characters). " +
        "Never invent a value: a value the table does not state is null. Never write a preschool identifier. " +
        "Never add rankings, quality judgements, opinions, staff names, pupil names or contact details.";

    public static string UserMessage(string gminaTeryt, string chunk) =>
        $"Gmina TERYT: {gminaTeryt}. Extract the rows from the table below.\n" +
        UntrustedText.Wrap("tuition-table", chunk);
}
