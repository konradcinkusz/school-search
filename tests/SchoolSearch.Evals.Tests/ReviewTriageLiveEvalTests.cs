using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Ai;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// LIVE: runs every review-triage case through the production pipeline with the real model, registered through the
/// production DI path (<c>AnthropicReviewTriageClient.Register</c>). Skipped unless EVALS_LIVE=1 and ANTHROPIC_API_KEY are
/// set. Hard rules must hold on every case; label agreement is held to a threshold. Degrade conditions that need a fake
/// model are reported as NOT RUN.
/// </summary>
public sealed class ReviewTriageLiveEvalTests
{
    /// <summary>PROPOSAL (AI-EVALS section 3, feature 4): label agreement of at least 85%. Not decided by the owner yet.</summary>
    private const double ProposedLabelAgreement = 0.85;

    [LiveEvalFact]
    public async Task Review_triage_hard_rules_hold_and_label_agreement_meets_the_proposed_threshold()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<AiTriageOptions>>(Options.Create(new AiTriageOptions()));
        AnthropicReviewTriageClient.Register(services, EvalLive.ApiKey);
        using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<IReviewTriageClient>();

        var report = new EvalReport();
        var hardFailures = new List<string>();
        var labelResults = new List<bool>();

        foreach (var c in EvalDatasets.Load(ReviewTriageEval.Feature))
        {
            if (ReviewTriageEval.OfflineOnly(c))
            {
                report.NotRun(ReviewTriageEval.Feature, c.Id, c.Class, "offline-only degrade condition");
                continue;
            }

            var run = await ReviewTriageHarness.RunAsync(c, model, CancellationToken.None);
            var checks = new List<GradeCheck>();
            checks.AddRange(ReviewTriageEval.GradePipeline(c, run).Checks);
            checks.AddRange(ReviewTriageEval.GradePrompt(c).Checks);

            if (run.Outcome.Status == ReviewTriageStatus.Triaged)
            {
                var label = ReviewTriageEval.GradeLabel(c, run.RawModelText);
                checks.AddRange(label.Checks);
                labelResults.Add(label.Checks.Single(check => check.Name == "label_acceptable").Pass);
            }

            var failed = checks.Where(check => !check.Pass).ToList();
            foreach (var hard in failed.Where(item => ReviewTriageEval.HardChecks.Contains(item.Name)))
            {
                hardFailures.Add($"{c.Id}: {hard.Name} [{hard.Detail}]");
            }

            report.Add(ReviewTriageEval.Feature, c.Id, c.Class, failed.Count == 0);
        }

        report.WriteSummary(ReviewTriageEval.Feature);

        Assert.Empty(hardFailures);
        var agreement = labelResults.Count == 0 ? 1.0 : labelResults.Count(pass => pass) / (double)labelResults.Count;
        Assert.True(agreement >= ProposedLabelAgreement, $"Label agreement {agreement:P1} is below the proposed {ProposedLabelAgreement:P0}.");
    }
}
