using SchoolSearch.Import.Ai;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Offline catchment-extract evals (no network, no key). The real extractor runs against a scripted model that gives the honest
/// reply for each case. The tests prove the dataset is valid, the grader accepts every honest run, refuses every mutant, and
/// that the pre-send screen stops a document before any model call.
/// </summary>
public sealed class ExtractorCatchmentEvalTests
{
    [Fact]
    public void Dataset_is_valid_covers_every_class_and_has_at_least_forty_cases()
    {
        Assert.Empty(EvalDatasetValidity.Problems(ExtractorCatchmentGrader.Feature));
    }

    [Fact]
    public async Task Every_case_produces_its_expected_outcome_and_the_honest_run_passes_every_check()
    {
        var failures = new List<string>();
        foreach (var c in Cases())
        {
            var run = await ExtractorCatchmentGrader.RunAsync(c, ExtractorCatchmentGrader.OfflineLlm(c), CancellationToken.None);
            var grade = ExtractorCatchmentGrader.Grade(c, run);
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
            var good = await ExtractorCatchmentGrader.RunAsync(c, ExtractorCatchmentGrader.OfflineLlm(c), CancellationToken.None);
            foreach (var (name, run) in ExtractorCatchmentGrader.Mutants(c, good))
            {
                if (ExtractorCatchmentGrader.Grade(c, run).Pass)
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
            var honest = ExtractorCatchmentGrader.HonestReply(c);
            var llm = new ExtractorScriptedLlm(() => new ImportLlmReply(honest, 100, 100));
            var run = await ExtractorCatchmentGrader.RunAsync(c, llm, CancellationToken.None);

            if (run.Outcome != "pre_send_refused" || llm.Calls.Count != 0)
            {
                failures.Add($"{c.Id}: outcome {run.Outcome}, model calls {llm.Calls.Count}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Every_prompt_wraps_the_document_in_one_untrusted_block_with_markup_escaped()
    {
        var failures = new List<string>();
        foreach (var c in Cases().Where(c => EvalJson.RequireString(c.Expect, "outcome") == "rows"))
        {
            var llm = new ExtractorScriptedLlm(() => new ImportLlmReply(ExtractorCatchmentGrader.HonestReply(c), 100, 100));
            await ExtractorCatchmentGrader.RunAsync(c, llm, CancellationToken.None);

            foreach (var (system, user) in llm.Calls)
            {
                var closes = user.Split("</untrusted>").Length - 1;
                var rawMarkupInDocument = EvalJson.RequireString(c.Input, "document").Contains("</untrusted>", StringComparison.Ordinal);
                if (closes != 1 || !system.Contains(UntrustedText.DataOnlyRule, StringComparison.Ordinal))
                {
                    failures.Add($"{c.Id}: {closes} closing tags in the user turn");
                }

                if (rawMarkupInDocument && !user.Contains("&lt;/untrusted&gt;", StringComparison.Ordinal))
                {
                    failures.Add($"{c.Id}: the document's closing tag was not escaped");
                }
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Hand_written_bad_rows_fail_the_rule_they_break()
    {
        var c = Cases().Single(item => item.Id == "catchment-extract-001");
        var good = await ExtractorCatchmentGrader.RunAsync(c, ExtractorCatchmentGrader.OfflineLlm(c), CancellationToken.None);
        var row = good.Rows.Single();

        var notVerbatim = good with { Rows = [row with { Quote = "ulicę Zielnikową, numery parzyste 2-20" }] };
        Assert.Contains(ExtractorCatchmentGrader.Grade(c, notVerbatim).Checks, check => check.Name == "quote_verbatim" && !check.Pass);

        var wrongNumber = good with { Rows = [row with { NumberTo = 20 }] };
        Assert.Contains(ExtractorCatchmentGrader.Grade(c, wrongNumber).Checks, check => check.Name == "gold_rows_found" && !check.Pass);

        var unlisted = good with { Rows = [row with { SchoolIdPodmiot = null }] };
        Assert.Contains(ExtractorCatchmentGrader.Grade(c, unlisted).Checks, check => check.Name == "school_id_exact" && !check.Pass);

        Assert.True(ExtractorCatchmentGrader.Grade(c, good).Pass);
    }

    private static IReadOnlyList<EvalCase> Cases() => EvalDatasets.Load(ExtractorCatchmentGrader.Feature);
}
