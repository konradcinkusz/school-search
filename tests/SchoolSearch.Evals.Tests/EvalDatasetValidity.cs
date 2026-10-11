using System.Text.RegularExpressions;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Structural checks every dataset must pass before any grader runs. Returns problems as text; an empty list is a pass.
/// Checks: file present and without a UTF-8 BOM, every line parses, ids well formed, prefixed by the feature and unique,
/// classes from AI-EVALS section 2 and all five covered, the minimum case count, a non-empty note on every case, and no
/// PII shape outside cases marked <c>synthetic_pii</c> (and a marked case must really contain one).
/// </summary>
public static class EvalDatasetValidity
{
    public const int MinimumCases = 40;

    private static readonly Regex IdShape = new(@"^[a-z0-9]+(-[a-z0-9]+)*-\d{3}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static IReadOnlyList<string> Problems(string feature)
    {
        var problems = new List<string>();
        var path = Path.Combine(EvalDatasets.DatasetsRoot, feature, "cases.jsonl");
        if (!File.Exists(path))
        {
            problems.Add($"{feature}: cases.jsonl is missing at {path}");
            return problems;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            problems.Add($"{feature}: cases.jsonl must not start with a UTF-8 BOM");
        }

        var cases = new List<EvalCase>();
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, System.Text.Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                cases.Add(EvalDatasets.ParseCase(line));
            }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
            {
                problems.Add($"{feature}: line {lineNumber} does not parse: {ex.Message}");
            }
        }

        if (cases.Count < MinimumCases)
        {
            problems.Add($"{feature}: {cases.Count} cases, at least {MinimumCases} required");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in cases)
        {
            if (!seen.Add(c.Id))
            {
                problems.Add($"{feature}: duplicate id {c.Id}");
            }

            if (!IdShape.IsMatch(c.Id) || !c.Id.StartsWith(feature + "-", StringComparison.Ordinal))
            {
                problems.Add($"{feature}: id {c.Id} must be '{feature}-' followed by three digits");
            }

            if (!EvalClasses.All.Contains(c.Class, StringComparer.Ordinal))
            {
                problems.Add($"{feature}: {c.Id} has class '{c.Class}', which is not an AI-EVALS section 2 class");
            }

            if (string.IsNullOrWhiteSpace(c.Notes))
            {
                problems.Add($"{feature}: {c.Id} has no notes (say what the case tests and where it came from)");
            }

            var text = c.Input.GetRawText() + "\n" + c.Expect.GetRawText() + "\n" + (c.Notes ?? string.Empty);
            var hasShape = EvalPii.LooksLikePii(text);
            if (!c.SyntheticPii && hasShape)
            {
                problems.Add($"{feature}: {c.Id} contains a personal-data shape but is not marked synthetic_pii");
            }

            if (c.SyntheticPii && !hasShape)
            {
                problems.Add($"{feature}: {c.Id} is marked synthetic_pii but contains no personal-data shape");
            }
        }

        foreach (var cls in EvalClasses.All)
        {
            if (!cases.Any(c => string.Equals(c.Class, cls, StringComparison.Ordinal)))
            {
                problems.Add($"{feature}: no case of class '{cls}'");
            }
        }

        return problems;
    }
}
