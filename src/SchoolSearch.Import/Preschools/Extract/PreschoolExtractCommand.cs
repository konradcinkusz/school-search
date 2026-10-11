using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SchoolSearch.Import.Ai;
using SchoolSearch.Import.Cke;

namespace SchoolSearch.Import.Preschools.Extract;

/// <summary>
/// <c>preschool-extract</c>: a SAVED gmina or operator tuition table -> candidate CSV in the <c>preschool-facts</c> layout,
/// a <c>.review.md</c> with every source quote, and a <c>.rejects.txt</c>. Reads files and calls the model only.
/// It never opens a DbContext and never writes the catalog; a human signs off and then runs <c>preschool-facts</c>.
/// </summary>
public static class PreschoolExtractCommand
{
    public const string Usage =
        "preschool-extract --input <saved text|html|csv> --gmina <TERYT> --source-name <text> --source-url <https url> " +
        "--as-of yyyy-MM-dd --preschools <csv with idPodmiot;name> --out <candidate.csv>";

    private const int MaxSourceNameLength = 200;
    private const int MaxSourceUrlLength = 500;
    private const string DateFormat = "yyyy-MM-dd";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    /// <summary>Validates every argument and file first (no key is read), then runs the extraction and writes the three files.</summary>
    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var request = ParseRequest(options);

        if (!ImportLlmSetup.TryCreate(out var llm, out var budget, out var reason))
        {
            Console.Error.WriteLine(reason);
            return 1;
        }

        Console.WriteLine(reason);

        var raw = await ReadTextAsync(request.InputPath, ct);
        var prepared = PreschoolExtractor.Prepare(raw);

        var list = PreschoolNameIndex.Load(await ReadTextAsync(request.PreschoolsPath, ct), request.Gmina);

        var result = await PreschoolExtractor.ExtractAsync(prepared, request.Gmina, request.AsOf, llm, list.Index, ct);
        result = result with { Rejects = list.Rejects.Concat(result.Rejects).ToList() };

        var modelLabel = Environment.GetEnvironmentVariable(ImportLlmSetup.ModelVariable)?.Trim();
        var meta = new PreschoolRenderMeta(
            request.SourceName,
            request.SourceUrl,
            request.AsOf,
            request.Gmina,
            Path.GetFileName(request.InputPath),
            string.IsNullOrWhiteSpace(modelLabel) ? ImportLlmSetup.DefaultModel : modelLabel,
            Path.GetFileName(request.RejectsPath));

        await File.WriteAllTextAsync(request.OutPath, PreschoolCandidateRender.ToCsv(result.Rows), Utf8WithoutBom, ct);
        await File.WriteAllTextAsync(request.ReviewPath, PreschoolCandidateRender.ToReview(result, meta), Utf8WithoutBom, ct);
        await File.WriteAllTextAsync(request.RejectsPath, PreschoolCandidateRender.ToRejects(result, request.SourceName), Utf8WithoutBom, ct);

        Console.WriteLine(
            $"preschool-extract: chunks={result.ChunkCount} failedChunks={result.FailedChunks} " +
            $"proposedRows={result.Rows.Count} rejected={result.Rejects.Count} " +
            $"staffLinesDropped={result.StaffLinesDropped} tokensIn={result.InputTokens} tokensOut={result.OutputTokens} " +
            $"budgetSpent={budget?.Spent.ToString(CultureInfo.InvariantCulture) ?? "n/a"}");
        Console.WriteLine($"wrote {request.OutPath}, {request.ReviewPath}, {request.RejectsPath}");
        Console.WriteLine("UNVERIFIED: nothing is loaded until a human signs off (see the review file). The catalog was not touched.");

        if (result.FailedChunks > 0)
        {
            Console.Error.WriteLine($"INCOMPLETE: {result.FailedChunks} chunk(s) gave no usable reply. Re-run before sign-off.");
            return 1;
        }

        return 0;
    }

    private sealed record Request(
        string InputPath,
        string Gmina,
        string SourceName,
        string SourceUrl,
        DateOnly AsOf,
        string PreschoolsPath,
        string OutPath,
        string ReviewPath,
        string RejectsPath);

    private static Request ParseRequest(IReadOnlyDictionary<string, string> options)
    {
        var gmina = Required(options, "gmina").Trim();
        if (!Regex.IsMatch(gmina, "^[0-9]{7}$", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("--gmina must be a 7-digit TERYT code.");
        }

        var sourceName = Required(options, "source-name").Trim();
        if (sourceName.Length == 0 || sourceName.Length > MaxSourceNameLength)
        {
            throw new ArgumentException($"--source-name is required and at most {MaxSourceNameLength} characters.");
        }

        var sourceUrl = Required(options, "source-url").Trim();
        if (!sourceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !CkeCsvSupport.IsAbsoluteHttpUrl(sourceUrl)
            || sourceUrl.Length > MaxSourceUrlLength)
        {
            throw new ArgumentException($"--source-url must be an absolute https URL of at most {MaxSourceUrlLength} characters.");
        }

        if (!DateOnly.TryParseExact(Required(options, "as-of"), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
        {
            throw new ArgumentException("--as-of must be a date in yyyy-MM-dd form.");
        }

        var inputPath = Required(options, "input");
        if (!File.Exists(inputPath))
        {
            throw new ArgumentException("--input does not point to an existing file.");
        }

        if (new FileInfo(inputPath).Length > ImportDocument.MaxInputBytes)
        {
            throw new ArgumentException($"--input is larger than {ImportDocument.MaxInputBytes} bytes.");
        }

        var preschoolsPath = Required(options, "preschools");
        if (!File.Exists(preschoolsPath))
        {
            throw new ArgumentException("--preschools does not point to an existing file.");
        }

        var outPath = Required(options, "out");
        if (!outPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--out must end in .csv.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (directory is null || !Directory.Exists(directory))
        {
            throw new ArgumentException("The directory of --out does not exist.");
        }

        var reviewPath = Path.ChangeExtension(outPath, ".review.md");
        var rejectsPath = Path.ChangeExtension(outPath, ".rejects.txt");
        foreach (var path in new[] { outPath, reviewPath, rejectsPath })
        {
            if (File.Exists(path))
            {
                throw new ArgumentException($"{Path.GetFileName(path)} already exists. Choose a new --out name so no signed-off file is overwritten.");
            }
        }

        return new Request(inputPath, gmina, sourceName, sourceUrl, asOf, preschoolsPath, outPath, reviewPath, rejectsPath);
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await CkeCsvSupport.ReadTextAsync(stream, ct);
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value)
            ? value
            : throw new ArgumentException($"Missing required option --{key}.");
}
