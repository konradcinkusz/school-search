using System.Globalization;
using System.Text;

namespace SchoolSearch.Import.Preschools.Extract;

/// <summary>Facts about one run that the review file states next to the rows.</summary>
public sealed record PreschoolRenderMeta(
    string SourceName,
    string SourceUrl,
    DateOnly AsOf,
    string GminaTeryt,
    string InputName,
    string ModelLabel,
    string RejectsFileName);

/// <summary>
/// Writes the three outputs as text. The candidate CSV is exactly the <c>preschool-facts</c> layout with no comment line
/// (the loader reads the first line as the header), no names, and <c>confidence = unverified</c> on every row.
/// </summary>
public static class PreschoolCandidateRender
{
    public const string CsvHeader = "idPodmiot;tuitionMin;tuitionMax;meal;opens;closes;freePlaces;freePlacesAsOf;confidence";

    public static string ToCsv(IReadOnlyList<CandidateRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        builder.Append(CsvHeader).Append('\n');
        foreach (var row in rows)
        {
            var fact = row.Fact;
            builder
                .Append(row.IdPodmiot.ToString(CultureInfo.InvariantCulture)).Append(';')
                .Append(Money(fact.TuitionMin)).Append(';')
                .Append(Money(fact.TuitionMax)).Append(';')
                .Append(Money(fact.Meal)).Append(';')
                .Append(Time(fact.Opens)).Append(';')
                .Append(Time(fact.Closes)).Append(';')
                .Append(fact.FreePlaces?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(';')
                .Append(Date(fact.FreePlacesAsOf)).Append(';')
                .Append(PreschoolFactsLoader.ConfidenceUnverified).Append('\n');
        }

        return builder.ToString();
    }

    public static string ToReview(PreschoolExtractResult result, PreschoolRenderMeta meta)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(meta);

        var text = new StringBuilder();
        text.AppendLine("# Preschool tuition candidates: UNVERIFIED");
        text.AppendLine();
        text.AppendLine("Wygenerowano przez AI. These rows are model proposals. Nothing here is loaded, and no person has checked them yet.");
        text.AppendLine();
        text.AppendLine("**UNVERIFIED - human sign-off required.** Check each row against the saved source file and its quote before you change its confidence to `reported`.");
        text.AppendLine();
        text.AppendLine($"- Source: {meta.SourceName} <{meta.SourceUrl}>, as of {Date(meta.AsOf)}");
        text.AppendLine($"- Gmina TERYT: {meta.GminaTeryt}");
        text.AppendLine($"- Input file: {meta.InputName}");
        text.AppendLine($"- Model: {meta.ModelLabel}; chunks sent: {result.ChunkCount}; chunks with no usable reply: {result.FailedChunks}");
        if (result.FailedChunks > 0)
        {
            text.AppendLine("- **INCOMPLETE:** some chunks gave no usable reply. Rows from the other chunks may still be missing. Re-run before sign-off.");
        }

        text.AppendLine($"- Tokens: input {result.InputTokens}, output {result.OutputTokens}");
        text.AppendLine($"- Staff-named lines removed before sending: {result.StaffLinesDropped}");
        text.AppendLine($"- Rows proposed: {result.Rows.Count}; rejected reasons in {meta.RejectsFileName} (not loaded, not listed here)");
        text.AppendLine();
        text.AppendLine("## Rows");

        var number = 0;
        foreach (var row in result.Rows)
        {
            number++;
            var fact = row.Fact;
            text.AppendLine();
            text.AppendLine($"### {number}. idPodmiot {row.IdPodmiot}: {row.MatchedName}");
            text.AppendLine($"- Name as written in the table: {fact.NameAsWritten}");
            text.AppendLine($"- Tuition min / max (PLN): {OrDash(Money(fact.TuitionMin))} / {OrDash(Money(fact.TuitionMax))}");
            text.AppendLine($"- Meal (PLN): {OrDash(Money(fact.Meal))}");
            text.AppendLine($"- Opens / closes: {OrDash(Time(fact.Opens))} / {OrDash(Time(fact.Closes))}");
            text.AppendLine($"- Free places: {(fact.FreePlaces?.ToString(CultureInfo.InvariantCulture) ?? "-")}" +
                (fact.FreePlacesAsOf is DateOnly asOf ? $" (as of {Date(asOf)})" : string.Empty));
            foreach (var note in fact.Notes)
            {
                text.AppendLine($"- Note: {note}");
            }

            text.AppendLine("- Source quote:");
            text.AppendLine($"  > {fact.Quote}");
            text.AppendLine("- [ ] values checked against the source file   [ ] confidence changed to `reported`");
        }

        text.AppendLine();
        text.AppendLine("## After sign-off");
        text.AppendLine();
        text.AppendLine("Edit the CSV: set `confidence` to `reported` only for rows you checked, and delete the others. Then load it with the existing command (the loader still validates every row and needs the catalog to be on the current schema):");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine($"SchoolSearch.Import preschool-facts --file <signed-off.csv> --source-name \"{meta.SourceName}\" --source-url \"{meta.SourceUrl}\" --as-of {Date(meta.AsOf)}");
        text.AppendLine("```");

        return text.ToString();
    }

    public static string ToRejects(PreschoolExtractResult result, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(result);

        var text = new StringBuilder();
        text.AppendLine($"Preschool extract rejects for: {sourceName}");
        text.AppendLine("NOT written to the candidate CSV. Nothing here is loaded. Check the source by hand if a row matters.");
        text.AppendLine();
        if (result.Rejects.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (var reason in result.Rejects)
        {
            text.AppendLine(reason);
        }

        return text.ToString();
    }

    private static string Money(decimal? value) =>
        value is decimal amount ? amount.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;

    private static string Time(TimeOnly? value) =>
        value is TimeOnly time ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    private static string Date(DateOnly? value) =>
        value is DateOnly date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string OrDash(string text) => text.Length == 0 ? "-" : text;
}
