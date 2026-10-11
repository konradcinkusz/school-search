using SchoolSearch.Api.Ai;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Offline checks on the correction-triage dataset (CI, no network, no key). Per case: the expectations are consistent, the
/// input guard and the model-call expectation agree, the production prompt keeps the note fenced, and the grader accepts a
/// good output built from the case's own allowed values (or the degrade output for refusal and max_tokens).
/// </summary>
public sealed class CorrectionTriageDatasetTests
{
    private static readonly string[] Outcomes = ["ok", "input_rejected", "output_rejected", "unavailable", "budget_exhausted"];

    public static IEnumerable<object[]> CaseIds() =>
        EvalDatasets.Load(CorrectionTriageEval.Feature).Select(c => new object[] { c.Id });

    [Fact]
    public void Dataset_passes_the_structural_validity_checks()
    {
        var problems = EvalDatasetValidity.Problems(CorrectionTriageEval.Feature);

        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Expectations_are_internally_consistent(string caseId)
    {
        var c = Find(caseId);
        var outcome = CorrectionTriageEval.Outcome(c);
        Assert.Contains(outcome, Outcomes);

        if (outcome != "ok")
        {
            return;
        }

        var categories = EvalJson.StringArray(c.Expect, "allowedCategories");
        var actions = EvalJson.StringArray(c.Expect, "allowedActions");
        Assert.NotEmpty(categories);
        Assert.NotEmpty(actions);
        Assert.All(categories, value => Assert.Contains(value, CorrectionTriageVocabulary.Categories));
        Assert.All(actions, value => Assert.Contains(value, CorrectionTriageVocabulary.SuggestedActions));
        if (categories.Contains(CorrectionTriageVocabulary.CategoryScoreDispute, StringComparer.Ordinal))
        {
            Assert.Contains(CorrectionTriageVocabulary.ActionRouteToMethodology, actions);
        }

        if (EvalJson.Flag(c.Expect, "scoreDispute"))
        {
            Assert.Contains(CorrectionTriageVocabulary.CategoryScoreDispute, categories);
        }
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void The_input_guard_and_the_model_call_match_the_outcome(string caseId)
    {
        var grade = CorrectionTriageEval.GradeInput(Find(caseId));

        Assert.True(grade.Pass, EvalGrade.Describe(grade));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void The_production_prompt_keeps_the_note_fenced_and_free_of_contacts(string caseId)
    {
        var c = Find(caseId);
        if (!CorrectionTriageEval.ModelCall(c))
        {
            return;
        }

        var grade = CorrectionTriageEval.GradePrompt(c);

        Assert.True(grade.Pass, EvalGrade.Describe(grade));
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void The_grader_accepts_a_good_output_and_the_degrade_output_for_each_case(string caseId)
    {
        var c = Find(caseId);
        var raw = CorrectionTriageEval.DegradeRawOutput(c);
        if (raw is null)
        {
            return;
        }

        var grade = CorrectionTriageEval.GradeOutput(c, raw);

        Assert.True(grade.Pass, EvalGrade.Describe(grade));
    }

    [Fact]
    public void A_contact_in_the_note_is_refused_before_any_model_call()
    {
        var c = Find("correction-triage-041");

        Assert.True(CorrectionTriageEval.GuardRejects(CorrectionTriageEval.ToInput(c)));
        Assert.Equal("input_rejected", CorrectionTriageEval.Outcome(c));
        Assert.False(CorrectionTriageEval.ModelCall(c));
    }

    private static EvalCase Find(string caseId) =>
        EvalDatasets.Load(CorrectionTriageEval.Feature).Single(c => c.Id == caseId);
}
