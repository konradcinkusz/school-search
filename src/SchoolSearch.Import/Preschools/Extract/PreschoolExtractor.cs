using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Preschools.Extract;

/// <summary>The text the model will see, split into chunks, after the staff-line filter and the pre-send check.</summary>
public sealed record PreparedSource(IReadOnlyList<string> Chunks, int StaffLinesDropped);

/// <summary>A validated fact matched exactly to one preschool in the list.</summary>
public sealed record CandidateRow(long IdPodmiot, string MatchedName, CandidateFact Fact);

/// <summary>Everything one run produced. Rejects hold every reason a row or a chunk was not written.</summary>
public sealed record PreschoolExtractResult(
    IReadOnlyList<CandidateRow> Rows,
    IReadOnlyList<string> Rejects,
    int ChunkCount,
    int FailedChunks,
    int StaffLinesDropped,
    long InputTokens,
    long OutputTokens);

/// <summary>
/// Pipeline for the offline preschool tuition extractor: saved document -> chunks -> model -> strict parse -> exact name
/// match. No database parameter exists anywhere in this path, so the extractor cannot write the catalog.
/// </summary>
public static class PreschoolExtractor
{
    public const int MaxChunkChars = 6000;
    public const int MaxChunks = 20;

    /// <summary>Lines naming staff are removed before sending (AI-FEATURES feature 6). The count is reported, never the text.</summary>
    private static readonly string[] StaffKeywords = ["dyrektor", "nauczyciel", "wychowawc", "pedagog", "psycholog"];

    /// <summary>
    /// Converts the saved file to text (HTML reduced to lines), drops staff lines, refuses personal-data patterns and
    /// splits the result. Throws <see cref="ArgumentException"/> when the file must not be sent or is too long.
    /// </summary>
    public static PreparedSource Prepare(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var kept = new List<string>();
        var dropped = 0;
        foreach (var line in ImportDocument.ToPlainText(raw).Split('\n'))
        {
            if (StaffKeywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                dropped++;
                continue;
            }

            kept.Add(line);
        }

        var text = string.Join('\n', kept);

        var hits = ImportPiiPrecheck.Find(text);
        if (hits.Count > 0)
        {
            throw new ArgumentException(
                $"Pre-send check refused the file: it looks like it contains {string.Join(", ", hits)}. " +
                "Nothing was sent. Remove those parts from the saved file and re-run.");
        }

        var chunks = ImportDocument.Chunk(text, MaxChunkChars);
        if (chunks.Count > MaxChunks)
        {
            throw new ArgumentException(
                $"The source makes {chunks.Count} chunks; the limit is {MaxChunks}. Split the saved file by section. Nothing was sent.");
        }

        return new PreparedSource(chunks, dropped);
    }

    /// <summary>
    /// Sends each chunk, parses each reply strictly and matches names exactly. A failed reply costs its chunk only.
    /// An exception from the model client stops the run (the caller writes nothing).
    /// </summary>
    public static async Task<PreschoolExtractResult> ExtractAsync(
        PreparedSource source,
        string gminaTeryt,
        DateOnly asOf,
        IImportLlm llm,
        PreschoolNameIndex index,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(index);

        var facts = new List<CandidateFact>();
        var rejects = new List<string>();
        var failed = 0;
        long inputTokens = 0;
        long outputTokens = 0;

        for (var i = 0; i < source.Chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = source.Chunks[i];

            var reply = await llm.CompleteAsync(
                PreschoolExtractPrompt.System,
                PreschoolExtractPrompt.UserMessage(gminaTeryt, chunk),
                PreschoolExtractPrompt.MaxOutputTokens,
                ct);

            inputTokens += reply.InputTokens;
            outputTokens += reply.OutputTokens;

            var parsed = PreschoolExtractParser.Parse(i + 1, chunk, reply.Text, asOf);
            if (parsed.Failed)
            {
                failed++;
            }

            rejects.AddRange(parsed.Rejects);
            facts.AddRange(parsed.Facts);
        }

        var matched = new List<CandidateRow>();
        foreach (var fact in facts)
        {
            var found = index.Find(fact.NameAsWritten);
            if (found.Count == 0)
            {
                rejects.Add($"chunk {fact.Chunk}: '{fact.NameAsWritten}' matches no preschool in --preschools (exact normalized name only; not guessed)");
            }
            else if (found.Count > 1)
            {
                rejects.Add($"chunk {fact.Chunk}: '{fact.NameAsWritten}' matches {found.Count} preschools in --preschools; not guessed");
            }
            else
            {
                matched.Add(new CandidateRow(found[0].IdPodmiot, found[0].Name, fact));
            }
        }

        // The same preschool twice in the table: both rows are held back, a human decides which one is right.
        var duplicates = matched
            .GroupBy(row => row.IdPodmiot)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet();

        foreach (var row in matched.Where(row => duplicates.Contains(row.IdPodmiot)))
        {
            rejects.Add($"chunk {row.Fact.Chunk}: idPodmiot {row.IdPodmiot} appears more than once in the proposed rows; both held back");
        }

        var rows = matched.Where(row => !duplicates.Contains(row.IdPodmiot)).ToList();

        return new PreschoolExtractResult(
            rows,
            rejects,
            source.Chunks.Count,
            failed,
            source.StaffLinesDropped,
            inputTokens,
            outputTokens);
    }
}
