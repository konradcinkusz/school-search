using SchoolSearch.Import.Ai;
using SchoolSearch.Import.Preschools.Extract;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Offline preschool-extract evals (no network, no key). The real pipeline runs against a scripted model that gives the honest
/// reply for each case. The tests prove the dataset is valid, the grader accepts every honest run, refuses every mutant, and that
/// the pre-send screen stops a table before any model call.
/// </summary>
public sealed class ExtractorPreschoolEvalTests
{
    [Fact]
    public void Dataset_is_valid_covers_every_class_and_has_at_least_forty_cases()
    {
        Assert.Empty(EvalDatasetValidity.Problems(ExtractorPreschoolGrader.Feature));
    }

    [Fact]
    public async Task Every_case_produces_its_expected_outcome_and_the_honest_run_passes_every_check()
    {
        var failures = new List<string>();
        foreach (var c in Cases())
        {
            var run = await ExtractorPreschoolGrader.RunAsync(c, ExtractorPreschoolGrader.OfflineLlm(c), CancellationToken.None);
            var grade = ExtractorPreschoolGrader.Grade(c, run);
            if (!grade.Pass)
            {
                failures.Add($"{c.Id}: {EvalGrade.Describe(grade)}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_mutant_of_an_honest_run_is_rejected_by_the_grader()
    {
        var survivors = new List<string>();
        foreach (var c in Cases().Where(c => EvalJson.RequireString(c.Expect, "outcome") == "rows"))
        {
            var good = await ExtractorPreschoolGrader.RunAsync(c, ExtractorPreschoolGrader.OfflineLlm(c), CancellationToken.None);
            foreach (var (name, run) in ExtractorPreschoolGrader.Mutants(c, good))
            {
                if (ExtractorPreschoolGrader.Grade(c, run).Pass)
                {
                    survivors.Add($"{c.Id}:{name}");
                }
            }
        }

        Assert.Empty(survivors);
    }

    [Fact]
    public async Task Pre_send_refusals_happen_before_any_model_call()
    {
        var failures = new List<string>();
        foreach (var c in Cases().Where(c => EvalJson.RequireString(c.Expect, "outcome") == "pre_send_refused"))
        {
            var honest = ExtractorPreschoolGrader.HonestReply(c);
            var llm = new ExtractorScriptedLlm(() => new ImportLlmReply(honest, 100, 100));
            var run = await ExtractorPreschoolGrader.RunAsync(c, llm, CancellationToken.None);

            if (run.Outcome != "pre_send_refused" || llm.Calls.Count != 0)
            {
                failures.Add($"{c.Id}: outcome {run.Outcome}, model calls {llm.Calls.Count}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task A_wrong_decimal_is_caught_as_an_amount_error_and_not_as_a_missing_row()
    {
        var c = Cases().Single(item => item.Id == "preschool-extract-006");
        var good = await ExtractorPreschoolGrader.RunAsync(c, ExtractorPreschoolGrader.OfflineLlm(c), CancellationToken.None);
        var result = good.Result!;
        var first = result.Rows[0];
        var shifted = first with { Fact = first.Fact with { TuitionMin = first.Fact.TuitionMin + 0.10m } };
        var changedRows = new List<CandidateRow> { shifted, result.Rows[1] };
        var changed = good with { Result = result with { Rows = changedRows } };

        var grade = ExtractorPreschoolGrader.Grade(c, changed);

        Assert.Contains(grade.Checks, check => check.Name == "amounts_exact" && !check.Pass);
        Assert.Contains(grade.Checks, check => check.Name == "gold_rows_found" && check.Pass);
    }

    [Fact]
    public void Comma_decimals_and_spaced_thousands_follow_the_product_amount_rule()
    {
        Assert.True(PreschoolMoney.TryParseAmount("1250,50", out var thousands));
        Assert.Equal(1250.50m, thousands);
        Assert.True(PreschoolMoney.TryParseAmount("450,5", out var short1));
        Assert.Equal(450.5m, short1);
        Assert.False(PreschoolMoney.TryParseAmount("450,505", out _));
        Assert.False(PreschoolMoney.TryParseAmount("1.250,50", out _));
        Assert.Contains(PreschoolMoney.AmountsIn("1 250,50 zł"), value => value == 1250.50m);
    }

    private static IReadOnlyList<EvalCase> Cases() => EvalDatasets.Load(ExtractorPreschoolGrader.Feature);
}
