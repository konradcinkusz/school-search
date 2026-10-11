using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Xunit.Abstractions;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Collects per-case results and prints per-class pass rates. Results are shared by every instance in the test run, so
/// a summary written by the last live test of a feature covers all of its cases. In live mode (<see cref="EvalLive.IsEnabled"/>)
/// <see cref="WriteSummary"/> also writes <c>evals/out/&lt;feature&gt;-&lt;utc&gt;.json</c> (gitignored). The report holds
/// case ids, classes and pass flags only: never a key, a model answer or review text.
/// </summary>
public sealed class EvalReport(ITestOutputHelper? output = null)
{
    private sealed record Entry(string Feature, string CaseId, string Class, bool? Pass, string? Reason);

    private static readonly ConcurrentQueue<Entry> Entries = new();

    /// <summary>Records one case: <paramref name="pass"/> true or false.</summary>
    public void Add(string feature, string caseId, string cls, bool pass) =>
        Entries.Enqueue(new Entry(feature, caseId, cls, pass, null));

    /// <summary>Records a case that could not run (for example a degrade condition the live service cannot produce). Reported, never counted as a pass.</summary>
    public void NotRun(string feature, string caseId, string cls, string reason) =>
        Entries.Enqueue(new Entry(feature, caseId, cls, null, reason));

    /// <summary>Prints the summary for one feature and, in live mode, writes the JSON report.</summary>
    public void WriteSummary(string feature)
    {
        var rows = Entries.Where(entry => entry.Feature == feature).ToList();
        var passed = rows.Count(row => row.Pass == true);
        var failed = rows.Count(row => row.Pass == false);
        var notRun = rows.Count(row => row.Pass is null);

        var lines = new List<string>
        {
            $"Eval {feature}: {passed} passed, {failed} failed, {notRun} not run, of {rows.Count} cases.",
        };

        var classes = rows.GroupBy(row => row.Class, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).ToList();
        foreach (var group in classes)
        {
            var ran = group.Count(row => row.Pass is not null);
            var ok = group.Count(row => row.Pass == true);
            var rate = ran == 0 ? 0.0 : 100.0 * ok / ran;
            lines.Add(string.Format(CultureInfo.InvariantCulture, "  {0}: {1}/{2} passed ({3:0.0}%), {4} not run", group.Key, ok, ran, rate, group.Count() - ran));
        }

        foreach (var line in lines)
        {
            if (output is null)
            {
                Console.WriteLine(line);
            }
            else
            {
                output.WriteLine(line);
            }
        }

        if (EvalLive.IsEnabled)
        {
            WriteJson(feature, rows, classes);
        }
    }

    private static void WriteJson(string feature, List<Entry> rows, List<IGrouping<string, Entry>> classes)
    {
        var folder = Path.Combine(EvalDatasets.RepoRoot, "evals", "out");
        Directory.CreateDirectory(folder);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var path = Path.Combine(folder, $"{feature}-{stamp}.json");

        var perClass = classes.ToDictionary(
            group => group.Key,
            group => new
            {
                passed = group.Count(row => row.Pass == true),
                ran = group.Count(row => row.Pass is not null),
                notRun = group.Count(row => row.Pass is null),
            },
            StringComparer.Ordinal);

        var report = new
        {
            feature,
            generatedUtc = DateTimeOffset.UtcNow,
            total = rows.Count,
            passed = rows.Count(row => row.Pass == true),
            failed = rows.Count(row => row.Pass == false),
            notRun = rows.Count(row => row.Pass is null),
            perClass,
            cases = rows.Select(row => new { id = row.CaseId, @class = row.Class, pass = row.Pass, reason = row.Reason }),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
