using SchoolSearch.Reviews.Ai;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Offline checks on the review-triage dataset (runs in CI, no network, no key). Each case is one test: the prompt fence
/// holds, the pipeline reaches its expected outcome with a canned model seam, and the label grader accepts the canned
/// label for every triaged case. Passing these proves the dataset and the graders agree with the production code.
/// </summary>
public sealed class ReviewTriageDatasetTests
{
    private const string StatusValues = "Triaged|Unavailable|BudgetExhausted|Failed";

    public static IEnumerable<object[]> CaseIds() =>
        EvalDatasets.Load(ReviewTriageEval.Feature).Select(c => new object[] { c.Id });

    [Fact]
    public void Dataset_passes_the_structural_validity_checks()
    {
        var problems = EvalDatasetValidity.Problems(ReviewTriageEval.Feature);

        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Expectations_are_internally_consistent(string caseId)
    {
        var c = Find(caseId);
        var acceptable = EvalJson.StringArray(c.Expect, "acceptable");
        var status = EvalJson.RequireString(c.Expect, "status");

        Assert.NotEmpty(acceptable);
        Assert.All(acceptable, label => Assert.Contains(label, ReviewTriageLabels.All));
        Assert.Contains(status, StatusValues.Split('|'));
        if (EvalJson.Flag(c.Expect, "containsPersonalName") || EvalJson.Flag(c.Expect, "injection"))
        {
            Assert.DoesNotContain(ReviewTriageLabels.Ok, acceptable);
        }
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Prompt_keeps_the_review_inside_the_untrusted_block(string caseId)
    {
        var grade = ReviewTriageEval.GradePrompt(Find(caseId));

        Assert.True(grade.Pass, EvalGrade.Describe(grade));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Pipeline_reaches_the_expected_outcome_with_a_canned_model(string caseId)
    {
        var c = Find(caseId);
        var run = await ReviewTriageHarness.RunAsync(c, new CannedReviewTriageClient(c), CancellationToken.None);

        var pipeline = ReviewTriageEval.GradePipeline(c, run);
        Assert.True(pipeline.Pass, EvalGrade.Describe(pipeline));

        if (run.Outcome.Status == ReviewTriageStatus.Triaged)
        {
            var label = ReviewTriageEval.GradeLabel(c, run.RawModelText);
            Assert.True(label.Pass, EvalGrade.Describe(label));
        }
    }

    [Fact]
    public async Task Triage_never_changes_the_review_status_or_writes_a_decision()
    {
        var c = Find("review-triage-006");
        var run = await ReviewTriageHarness.RunAsync(c, new CannedReviewTriageClient(c), CancellationToken.None);

        Assert.Equal("pending", run.StatusBefore);
        Assert.Equal("pending", run.StatusAfter);
        Assert.Equal(0, run.DecisionRows);
        Assert.Equal(1, run.TriageRows);
    }

    [Fact]
    public async Task Over_the_input_limit_the_review_is_unclear_and_the_model_is_not_called()
    {
        var c = Find("review-triage-055");
        var run = await ReviewTriageHarness.RunAsync(c, new CannedReviewTriageClient(c), CancellationToken.None);

        Assert.False(run.ModelCalled);
        Assert.Equal(ReviewTriageStatus.Triaged, run.Outcome.Status);
        Assert.Equal(ReviewTriageLabels.Unclear, run.Outcome.Label);
    }

    private static EvalCase Find(string caseId) =>
        EvalDatasets.Load(ReviewTriageEval.Feature).Single(c => c.Id == caseId);
}
