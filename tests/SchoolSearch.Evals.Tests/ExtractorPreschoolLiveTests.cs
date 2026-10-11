using SchoolSearch.Import.Ai;
using Xunit.Abstractions;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Live preschool-extract eval against the real model. It uses <see cref="ImportLlmSetup.TryCreate"/>, the production client with
/// its run budget, then the same pipeline and checks as the offline eval. Skipped unless EVALS_LIVE=1 and ANTHROPIC_API_KEY are set.
/// Degradation cases run offline only and are reported as not run.
/// </summary>
public sealed class ExtractorPreschoolLiveTests(ITestOutputHelper output)
{
    [LiveEvalFact]
    public async Task Live_tables_yield_exact_amounts_and_never_invented_rows()
    {
        Assert.True(ImportLlmSetup.TryCreate(out var llm, out _, out var reason), reason);

        var report = new EvalReport(output);
        var hardFailures = new List<string>();
        foreach (var c in EvalDatasets.Load(ExtractorPreschoolGrader.Feature))
        {
            var mode = EvalJson.RequireString(c.Input, "modelMode");
            if (mode != "normal")
            {
                report.NotRun(ExtractorPreschoolGrader.Feature, c.Id, c.Class, "degradation runs offline only");
                continue;
            }

            var run = await ExtractorPreschoolGrader.RunAsync(c, llm, CancellationToken.None);
            var grade = ExtractorPreschoolGrader.Grade(c, run);
            report.Add(ExtractorPreschoolGrader.Feature, c.Id, c.Class, grade.Pass);

            if (grade.Checks.Any(check => !check.Pass && ExtractorPreschoolGrader.HardChecks.Contains(check.Name)))
            {
                hardFailures.Add($"{c.Id}: {EvalGrade.Describe(grade)}");
            }
        }

        report.WriteSummary(ExtractorPreschoolGrader.Feature);
        Assert.Empty(hardFailures);
    }
}
