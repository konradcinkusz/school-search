using System.Text;
using System.Text.Json;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Loads <c>evals/datasets/&lt;feature&gt;/cases.jsonl</c> from the repository root (the folder above the one that holds
/// SchoolSearch.sln). Nothing is copied into the build output. A missing file or a malformed line is a test failure.
/// </summary>
public static class EvalDatasets
{
    public const string SolutionFileName = "SchoolSearch.sln";

    public static string DatasetsRoot { get; } = Path.Combine(FindRepoRoot(), "evals", "datasets");

    /// <summary>The repository root, used by <see cref="EvalReport"/> for the live output folder.</summary>
    internal static string RepoRoot => FindRepoRoot();

    public static IReadOnlyList<EvalCase> Load(string feature)
    {
        if (string.IsNullOrEmpty(feature) || !feature.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
        {
            throw new ArgumentException("Feature names are lowercase letters, digits and hyphens.", nameof(feature));
        }

        var path = Path.Combine(DatasetsRoot, feature, "cases.jsonl");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Eval dataset not found.", path);
        }

        var cases = new List<EvalCase>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                cases.Add(ParseCase(line));
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                throw new FormatException($"{path} line {lineNumber}: {ex.Message}", ex);
            }
        }

        return cases;
    }

    /// <summary>Reads one JSON line into a case. Used by <see cref="Load"/> and by tests that build cases inline.</summary>
    public static EvalCase ParseCase(string jsonLine)
    {
        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("A case must be a JSON object.");
        }

        var id = EvalJson.RequireString(root, "id");
        var cls = EvalJson.RequireString(root, "class");
        var input = EvalJson.RequireObject(root, "input");
        var expect = EvalJson.RequireObject(root, "expect");
        var notes = EvalJson.OptionalString(root, "notes");
        var syntheticPii = EvalJson.Flag(root, "synthetic_pii");

        return new EvalCase(id, cls, input.Clone(), expect.Clone(), notes, syntheticPii);
    }

    internal static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{SolutionFileName} not found above {AppContext.BaseDirectory}.");
    }
}

/// <summary>The scenario classes of AI-EVALS section 2, with the exact names used in every dataset.</summary>
public static class EvalClasses
{
    public const string HappyPath = "Happy path";
    public const string Ambiguity = "Ambiguity";
    public const string DeniedPath = "Denied path";
    public const string Adversarial = "Adversarial";
    public const string Degradation = "Degradation";

    public static readonly string[] All = [HappyPath, Ambiguity, DeniedPath, Adversarial, Degradation];
}
