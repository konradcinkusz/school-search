using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Ai;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// LIVE: runs the correction-triage cases with the real model, through the production prompt builder and the real client
/// registered by <c>AnthropicCorrectionTriageClient.Register</c>. The service's database and cache steps are not in this
/// path; the input guard and the parser are graded as the service runs them. Skipped unless EVALS_LIVE=1 and
/// ANTHROPIC_API_KEY are set. Hard rules must hold; category agreement is held to a threshold. Degrade conditions are NOT RUN.
/// </summary>
public sealed class CorrectionTriageLiveEvalTests
{
    /// <summary>PROPOSAL (AI-EVALS section 3, feature 5): category agreement of at least 80%. Not decided by the owner yet.</summary>
    private const double ProposedCategoryAgreement = 0.80;

    [LiveEvalFact]
    public async Task Correction_triage_hard_rules_hold_and_category_agreement_meets_the_proposed_threshold()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AiOptions>>(Options.Create(new AiOptions()));
        AnthropicCorrectionTriageClient.Register(services, EvalLive.ApiKey);
        using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<ICorrectionTriageClient>();

        var report = new EvalReport();
        var hardFailures = new List<string>();
        var categoryResults = new List<bool>();

        foreach (var c in EvalDatasets.Load(CorrectionTriageEval.Feature))
        {
            if (CorrectionTriageEval.OfflineOnly(c))
            {
                report.NotRun(CorrectionTriageEval.Feature, c.Id, c.Class, "offline-only degrade condition");
                continue;
            }

            var checks = new List<GradeCheck>(CorrectionTriageEval.GradeInput(c).Checks);
            if (CorrectionTriageEval.ModelCall(c))
            {
                var prompt = CorrectionTriagePromptBuilder.Build(CorrectionTriageEval.ToInput(c));
                checks.AddRange(CorrectionTriageEval.GradePrompt(c, prompt).Checks);

                var completion = await model.CompleteAsync(prompt.System, prompt.User, CancellationToken.None);
                var output = CorrectionTriageEval.GradeOutput(c, completion.Text);
                checks.AddRange(output.Checks);

                if (CorrectionTriageEval.Outcome(c) == "ok")
                {
                    categoryResults.Add(output.Checks.Single(check => check.Name == "category_allowed").Pass);
                }
            }

            var failed = checks.Where(check => !check.Pass).ToList();
            foreach (var hard in failed.Where(item => CorrectionTriageEval.HardChecks.Contains(item.Name)))
            {
                hardFailures.Add($"{c.Id}: {hard.Name} [{hard.Detail}]");
            }

            report.Add(CorrectionTriageEval.Feature, c.Id, c.Class, failed.Count == 0);
        }

        report.WriteSummary(CorrectionTriageEval.Feature);

        Assert.Empty(hardFailures);
        var agreement = categoryResults.Count == 0 ? 1.0 : categoryResults.Count(pass => pass) / (double)categoryResults.Count;
        Assert.True(agreement >= ProposedCategoryAgreement, $"Category agreement {agreement:P1} is below the proposed {ProposedCategoryAgreement:P0}.");
    }
}
