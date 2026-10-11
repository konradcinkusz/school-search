using System.Globalization;
using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Catchment.Extract;

/// <summary>
/// The <c>catchment-extract</c> command. Reads a SAVED resolution, asks the model for candidate street rules, and writes
/// a candidate CSV plus a review file. It never fetches a URL, never opens a DbContext and never loads anything: a human
/// signs off the review and then runs the normal <c>catchment</c> command.
/// </summary>
public static class CatchmentExtractCommand
{
    /// <summary>Runs the command. Returns the process exit code (0 ok, 1 error). Argument problems throw ArgumentException.</summary>
    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string> options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var input = Required(options, "input");
        var gmina = Required(options, "gmina");
        var resolutionUrl = Required(options, "resolution-url");
        var resolutionDateText = Required(options, "resolution-date");
        var outPath = Required(options, "out");
        var schoolListPath = Optional(options, "map-schools");

        if (gmina.Length != 7 || !gmina.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("--gmina must be exactly 7 digits (TERYT).");
        }

        if (!Uri.TryCreate(resolutionUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || resolutionUrl.Length > 500)
        {
            throw new ArgumentException("--resolution-url must be an absolute http(s) URL of at most 500 characters.");
        }

        var resolutionDate = ParseDate(resolutionDateText, "--resolution-date");
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        if (!outPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--out must end with .csv");
        }

        var reviewPath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? string.Empty,
            Path.GetFileNameWithoutExtension(outPath) + ".review.md");

        if (File.Exists(outPath) || File.Exists(reviewPath))
        {
            Console.Error.WriteLine("Output exists. Choose a new --out name so a signed-off candidate is never overwritten. Nothing was written.");
            return Program.ExitError;
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine("Input file not found. Nothing was written.");
            return Program.ExitError;
        }

        if (new FileInfo(input).Length > ImportDocument.MaxInputBytes)
        {
            Console.Error.WriteLine($"Input is larger than {ImportDocument.MaxInputBytes / (1024 * 1024)} MB. Split it by article. Nothing was written.");
            return Program.ExitError;
        }

        var text = ImportDocument.ToPlainText(await File.ReadAllTextAsync(input, ct));

        var personal = ImportPiiPrecheck.Find(text);
        if (personal.Count > 0)
        {
            // Pattern names only: the matched text is never printed.
            Console.Error.WriteLine(
                "Refusing the document: it looks like it carries personal data (" + string.Join("; ", personal) +
                "). Remove the personal data from the saved file and run again. Nothing was sent or written.");
            return Program.ExitError;
        }

        if (!ImportLlmSetup.TryCreate(out var llm, out _, out var setupReason))
        {
            Console.Error.WriteLine(setupReason);
            return Program.ExitError;
        }

        SchoolNameIndex? schools = null;
        if (schoolListPath is not null)
        {
            schools = SchoolNameIndex.LoadFile(schoolListPath);
        }

        Console.WriteLine(setupReason);

        var extractor = new CatchmentExtractor(llm);
        var result = await extractor.RunAsync(text, gmina, schools, ct);

        var model = Environment.GetEnvironmentVariable(ImportLlmSetup.ModelVariable)?.Trim();
        var meta = new CandidateMeta(
            gmina,
            resolutionUrl,
            resolutionDate,
            asOf,
            string.IsNullOrWhiteSpace(model) ? ImportLlmSetup.DefaultModel : model);

        var csv = CatchmentCandidateWriter.BuildCsv(result.Rows, meta);
        var review = CatchmentCandidateWriter.BuildReview(
            result.Rows, result.Rejected, meta, result.Chunks, result.InputTokens, result.OutputTokens);

        await File.WriteAllTextAsync(outPath, csv, ct);
        await File.WriteAllTextAsync(reviewPath, review, ct);

        Console.WriteLine(
            $"catchment-extract: parts={result.Chunks} candidates={result.Rows.Count} rejected={result.Rejected.Count} " +
            $"tokens-in={result.InputTokens} tokens-out={result.OutputTokens} (candidate written; not loaded)");
        Console.WriteLine($"  candidate csv: {outPath}");
        Console.WriteLine($"  review file:   {reviewPath}");
        Console.WriteLine("  UNVERIFIED - human sign-off required before any load.");

        if (schools is null)
        {
            Console.WriteLine("  No --map-schools given: schoolIdPodmiot is empty in every row; the loader will reject until filled in.");
        }

        return Program.ExitOk;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required option --{key}.");

    private static string? Optional(IReadOnlyDictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) ? value : null;

    private static DateOnly ParseDate(string text, string option) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new ArgumentException($"{option} must be a date in yyyy-MM-dd form.");
}
