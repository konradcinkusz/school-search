using Anthropic;
using SchoolSearch.Billing.Ai;
using Xunit.Abstractions;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Live profile-copy eval against the real model. It uses the production prompt builder and the production
/// <see cref="AnthropicDraftClient"/>, then the same parser, linter and digit check as the service. Skipped unless
/// EVALS_LIVE=1 and ANTHROPIC_API_KEY are set. Degradation cases run offline only and are reported as not run.
/// </summary>
public sealed class ProfileCopyLiveTests(ITestOutputHelper output)
{
    [LiveEvalFact]
    public async Task Live_drafts_hold_every_hard_boundary()
    {
        var report = new EvalReport(output);
        var client = new AnthropicDraftClient(
            new AnthropicClient { ApiKey = EvalLive.ApiKey },
            Microsoft.Extensions.Options.Options.Create(new AiDraftOptions()));

        var hardFailures = new List<string>();
        foreach (var c in EvalDatasets.Load(ProfileCopyGrader.Feature))
        {
            var outcome = ProfileCopyGrader.Outcome(c);
            if (outcome == "refused_input")
            {
                // A guard refuses before any call: the model is not reached, so no key is spent on this case.
                var guard = ProfileCopyGrader.Grade(c, null);
                report.Add(ProfileCopyGrader.Feature, c.Id, c.Class, guard.Pass);
                if (!guard.Pass)
                {
                    hardFailures.Add($"{c.Id}: {EvalGrade.Describe(guard)}");
                }

                continue;
            }

            if (outcome != "draft")
            {
                report.NotRun(ProfileCopyGrader.Feature, c.Id, c.Class, "degradation runs offline only");
                continue;
            }

            var prompt = DraftPromptBuilder.Build(ProfileCopyGrader.Bullets(c));
            var completion = await client.CompleteAsync(prompt.System, prompt.User, CancellationToken.None);
            var grade = ProfileCopyGrader.Grade(c, completion.Text);
            report.Add(ProfileCopyGrader.Feature, c.Id, c.Class, grade.Pass);

            if (grade.Checks.Any(check => !check.Pass && ProfileCopyGrader.HardChecks.Contains(check.Name)))
            {
                hardFailures.Add($"{c.Id}: {EvalGrade.Describe(grade)}");
            }
        }

        report.WriteSummary(ProfileCopyGrader.Feature);
        Assert.Empty(hardFailures);
    }
}
