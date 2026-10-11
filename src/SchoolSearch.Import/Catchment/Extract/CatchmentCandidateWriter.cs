using System.Globalization;
using System.Text;

namespace SchoolSearch.Import.Catchment.Extract;

/// <summary>Coverage fields shared by every candidate row of one gmina resolution.</summary>
/// <param name="Gmina">Seven-digit TERYT.</param>
/// <param name="ResolutionUrl">Public link to the resolution (becomes resolutionUrl and sourceUrl).</param>
/// <param name="ResolutionDate">Date the council adopted the resolution.</param>
/// <param name="AsOf">Date the saved file was made. Must not be in the future.</param>
/// <param name="Model">Model id used, recorded in the review file.</param>
public sealed record CandidateMeta(
    string Gmina,
    string ResolutionUrl,
    DateOnly ResolutionDate,
    DateOnly AsOf,
    string Model);

/// <summary>
/// Writes the candidate CSV in the exact column order the existing <c>catchment</c> loader reads, plus a row-level
/// <c>confidence</c> column (extra columns are ignored by the loader), and the sibling review file with every row and
/// its source quote. The method text carries "UNVERIFIED" so a candidate that is loaded without sign-off is visible.
/// </summary>
/// <remarks>
/// The loader does not accept comment lines, so the "UNVERIFIED - human sign-off required" notice is in the method
/// column and at the top of the review file, not as a header comment.
/// </remarks>
public static class CatchmentCandidateWriter
{
    public const string Source = "gmina resolution";
    public const string UnverifiedMethod = "llm-extract-v1; UNVERIFIED - human sign-off required";

    public static readonly string[] CsvColumns =
    [
        "gminaTeryt", "schoolIdPodmiot", "street", "numberFrom", "numberTo", "parity", "state",
        "resolutionUrl", "resolutionDate", "source", "sourceUrl", "asOf", "method", "confidence",
    ];

    public static string BuildCsv(IReadOnlyList<CandidateRow> rows, CandidateMeta meta)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(meta);

        var builder = new StringBuilder();
        builder.Append(string.Join(',', CsvColumns)).Append('\n');

        foreach (var row in rows)
        {
            var fields = new[]
            {
                meta.Gmina,
                row.SchoolIdPodmiot?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.Street,
                row.NumberFrom?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.NumberTo?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.Parity ?? string.Empty,
                "street_list",
                meta.ResolutionUrl,
                meta.ResolutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Source,
                meta.ResolutionUrl,
                meta.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                UnverifiedMethod,
                row.Confidence,
            };

            builder.Append(string.Join(',', fields.Select(CsvField))).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>The review file: every candidate with its source quote, every rejection, and the sign-off steps.</summary>
    public static string BuildReview(
        IReadOnlyList<CandidateRow> rows,
        IReadOnlyList<RejectedCandidate> rejected,
        CandidateMeta meta,
        int chunks,
        long inputTokens,
        long outputTokens)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(meta);

        var md = new StringBuilder();
        md.AppendLine($"# Catchment candidates for gmina {meta.Gmina}");
        md.AppendLine();
        md.AppendLine("**UNVERIFIED - human sign-off required.** These rows were proposed by a model from a saved document.");
        md.AppendLine("They are not catchment facts, are not published, and are not loaded. Check every row against its source quote.");
        md.AppendLine();
        md.AppendLine($"- Resolution: {meta.ResolutionUrl} (adopted {meta.ResolutionDate:yyyy-MM-dd})");
        md.AppendLine($"- Saved file date (asOf): {meta.AsOf:yyyy-MM-dd}");
        md.AppendLine($"- Model: {meta.Model}; parts read: {chunks}; tokens in/out: {inputTokens}/{outputTokens}");
        md.AppendLine($"- Candidate rows in the CSV: {rows.Count}; rejected by validation: {rejected.Count}");
        md.AppendLine();
        md.AppendLine("## Rows (CSV order)");
        md.AppendLine();
        md.AppendLine("| # | School as written | schoolIdPodmiot | Street | From | To | Parity | Confidence | Part | Note |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            md.AppendLine(
                $"| R{i + 1} | {Cell(r.SchoolAsWritten)} | {r.SchoolIdPodmiot?.ToString(CultureInfo.InvariantCulture) ?? "(fill in)"} " +
                $"| {Cell(r.Street)} | {r.NumberFrom?.ToString(CultureInfo.InvariantCulture) ?? string.Empty} " +
                $"| {r.NumberTo?.ToString(CultureInfo.InvariantCulture) ?? string.Empty} | {r.Parity ?? string.Empty} " +
                $"| {r.Confidence} | {r.Chunk} | {Cell(r.Note ?? string.Empty)} |");
        }

        md.AppendLine();
        md.AppendLine("## Source quotes");
        md.AppendLine();

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            md.AppendLine($"**R{i + 1}** (part {r.Chunk}; conditions: {r.Conditions ?? "none stated"})");
            md.AppendLine();
            md.AppendLine($"> {r.Quote}");
            md.AppendLine();
        }

        md.AppendLine("## Rejected by validation (not in the CSV)");
        md.AppendLine();

        if (rejected.Count == 0)
        {
            md.AppendLine("None.");
        }
        else
        {
            foreach (var rj in rejected)
            {
                md.AppendLine($"- part {rj.Chunk}: {rj.Reason}");
            }
        }

        md.AppendLine();
        md.AppendLine("## Before the loader runs");
        md.AppendLine();
        md.AppendLine("1. Open the resolution at the link above and check each row and its quote, including the house-number rules.");
        md.AppendLine("2. Fill in every empty schoolIdPodmiot. The loader rejects the whole gmina while any row is incomplete.");
        md.AppendLine("3. Remove any row that is wrong. Do not add rows the model did not propose without a source.");
        md.AppendLine("4. Change the method column to `llm-extract-v1 + human-verified` only after sign-off, and record who signed off.");
        md.AppendLine("5. Load with `SchoolSearch.Import catchment --file <csv>`. The file is never loaded by the extractor.");

        return md.ToString();
    }

    private static string CsvField(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>A Markdown table cell: one line, pipes escaped.</summary>
    private static string Cell(string text) =>
        text.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal);
}
