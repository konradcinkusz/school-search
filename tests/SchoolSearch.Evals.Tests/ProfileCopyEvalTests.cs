using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolSearch.Billing.Ai;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// Offline profile-copy evals (no network, no key). They prove the dataset is valid, that the grader accepts each hand-written
/// reference and refuses each mutant, and that the degradation scenarios run through the real <see cref="ProfileDraftService"/>.
/// </summary>
public sealed class ProfileCopyEvalTests
{
    private const string Slug = "szkola-podstawowa-testowa-alfa";
    private const string OwnerHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string ValidReply = ProfileCopyGrader.ReplyJson("Boisko szkolne", "Szkoła ma boisko szkolne.");

    [Fact]
    public void Dataset_is_valid_covers_every_class_and_has_at_least_forty_cases()
    {
        Assert.Empty(EvalDatasetValidity.Problems(ProfileCopyGrader.Feature));
    }

    [Fact]
    public void Each_reference_answer_passes_and_each_refused_input_is_refused_by_its_named_guard()
    {
        var failures = new List<string>();
        foreach (var c in Cases())
        {
            switch (ProfileCopyGrader.Outcome(c))
            {
                case "draft":
                    var (headline, description) = ProfileCopyGrader.Reference(c);
                    var grade = ProfileCopyGrader.Grade(c, ProfileCopyGrader.ReplyJson(headline, description));
                    if (!grade.Pass)
                    {
                        failures.Add($"{c.Id}: {EvalGrade.Describe(grade)}");
                    }

                    break;
                case "refused_input":
                    var refused = ProfileCopyGrader.Grade(c, null);
                    if (!refused.Pass)
                    {
                        failures.Add($"{c.Id}: {EvalGrade.Describe(refused)}");
                    }

                    break;
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Every_mutant_of_a_reference_answer_is_rejected_by_the_grader()
    {
        var survivors = new List<string>();
        foreach (var c in Cases().Where(c => ProfileCopyGrader.Outcome(c) == "draft"))
        {
            foreach (var (name, text) in ProfileCopyGrader.Mutants(c))
            {
                if (ProfileCopyGrader.Grade(c, text).Pass)
                {
                    survivors.Add($"{c.Id}:{name}");
                }
            }
        }

        Assert.Empty(survivors);
    }

    [Fact]
    public void Hand_written_bad_answers_fail_the_rule_they_break()
    {
        var happy = Case("profile-copy-001");

        var superlativeHeadline = ProfileCopyGrader.Grade(happy, ProfileCopyGrader.ReplyJson("Najlepsza szkoła", "Szkoła prowadzi klasy sportowe od 4 klasy."));
        Assert.True(ProfileCopyGrader.Failed(superlativeHeadline, "claim_linter"));

        var invented = ProfileCopyGrader.Grade(happy, ProfileCopyGrader.ReplyJson("Szkoła z klasami sportowymi", "Szkoła ma 2030 uczniów w klasach sportowych."));
        Assert.True(ProfileCopyGrader.Failed(invented, "digits_grounded"));

        var markup = ProfileCopyGrader.Grade(happy, "{\"headline\":\"<b>Szkoła</b>\",\"description\":\"Klasy sportowe.\"}");
        Assert.True(ProfileCopyGrader.Failed(markup, "parse_ok"));

        var guarantee = ProfileCopyGrader.Grade(happy, ProfileCopyGrader.ReplyJson("Szkoła z klasami sportowymi", "Gwarantujemy sukces w klasach sportowych."));
        Assert.True(ProfileCopyGrader.Failed(guarantee, "claim_linter"));
    }

    [Fact]
    public void A_correct_draft_passes_every_check_including_the_soft_ones()
    {
        var happy = Case("profile-copy-001");
        var (headline, description) = ProfileCopyGrader.Reference(happy);

        var grade = ProfileCopyGrader.Grade(happy, ProfileCopyGrader.ReplyJson(headline, description));

        Assert.True(grade.Pass, EvalGrade.Describe(grade));
    }

    [Fact]
    public async Task Degraded_scenarios_answer_with_the_fixed_outcome_and_never_a_fabricated_draft()
    {
        var failures = new List<string>();
        foreach (var c in Cases().Where(c => c.Class == EvalClasses.Degradation))
        {
            var run = await RunScenarioAsync(c);
            var expectedCalls = ExtractorSupport.Int(c.Expect, "modelCalls");
            var status = StatusName(run.Status);

            if (status != ProfileCopyGrader.Outcome(c) || run.Calls != expectedCalls)
            {
                failures.Add($"{c.Id}: got {status} with {run.Calls} calls");
            }

            if (run.Draft is not null)
            {
                var grade = ProfileCopyGrader.Grade(c, ProfileCopyGrader.ReplyJson(run.Draft.Headline, run.Draft.Description));
                if (!grade.Pass)
                {
                    failures.Add($"{c.Id}: drafted text failed the grader: {EvalGrade.Describe(grade)}");
                }
            }
        }

        Assert.Empty(failures);
    }

    private static IReadOnlyList<EvalCase> Cases() => EvalDatasets.Load(ProfileCopyGrader.Feature);

    private static EvalCase Case(string id) => Cases().Single(c => c.Id == id);

    private static string StatusName(ProfileDraftStatus status) => status switch
    {
        ProfileDraftStatus.Drafted => "draft",
        ProfileDraftStatus.Unavailable => "unavailable",
        ProfileDraftStatus.BudgetExhausted => "budget_exhausted",
        ProfileDraftStatus.Rejected => "rejected",
        _ => status.ToString(),
    };

    /// <summary>Runs one degradation scenario through the production service with a scripted client. No network.</summary>
    private static async Task<(ProfileDraftStatus Status, int Calls, DraftCandidate? Draft)> RunScenarioAsync(EvalCase c)
    {
        var scenario = ProfileCopyGrader.Scenario(c);
        Func<int, DraftCompletion> respond = _ => new DraftCompletion(ValidReply, 100, 40);
        if (scenario == "model_throws")
        {
            respond = _ => throw new InvalidOperationException("model call failed; the text is never shown");
        }
        else if (scenario == "model_refusal")
        {
            respond = _ => new DraftCompletion(string.Empty, 100, 0);
        }
        else if (scenario == "model_prose_then_valid")
        {
            respond = call => new DraftCompletion(call == 1 ? "Oto propozycja bez JSON." : ValidReply, 100, 40);
        }

        var client = new ScriptedDraftClient(respond);

        var available = scenario is not ("no_key" or "ai_disabled");
        var options = Microsoft.Extensions.Options.Options.Create(new AiDraftOptions
        {
            DailyTokenBudget = scenario == "budget_exhausted" ? 0 : 100_000,
        });
        var clock = new FixedClock(Now);
        using var limiter = new DraftRateLimiter(options);
        using var db = CreateDb();
        await SeedEntitlementAsync(db);

        var service = new ProfileDraftService(
            db,
            client,
            new DraftTokenBudget(clock, options),
            limiter,
            new DraftAvailability(available, available ? "configured" : "switched off for this scenario"),
            clock,
            NullLogger<ProfileDraftService>.Instance);

        var outcome = await service.DraftAsync(
            Slug,
            OwnerHash,
            new ProfileDraftRequest(ProfileCopyGrader.Bullets(c).ToArray()),
            CancellationToken.None);

        return (outcome.Status, client.Calls, outcome.Draft);
    }

    private static BillingDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase("evals-profile-copy-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new BillingDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>An approved claim and an active subscription for the owner, so the entitlement check passes.</summary>
    private static async Task SeedEntitlementAsync(BillingDbContext db)
    {
        db.SchoolClaims.Add(new SchoolClaim
        {
            Id = Guid.NewGuid(),
            SchoolSlug = Slug,
            OwnerHash = OwnerHash,
            RoleDescription = "Dyrekcja",
            Status = "approved",
            CreatedAt = Now,
        });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            SchoolSlug = Slug,
            OwnerHash = OwnerHash,
            PlanCode = "test-plan",
            Status = "active",
            CurrentPeriodEnd = Now.AddDays(30),
            CancelAtPeriodEnd = false,
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Answers with <c>respond(callNumber)</c> and counts the calls. Records nothing else.</summary>
    private sealed class ScriptedDraftClient(Func<int, DraftCompletion> respond) : IDraftClient
    {
        private int _calls;

        public int Calls => _calls;

        public Task<DraftCompletion> CompleteAsync(string system, string user, CancellationToken ct)
        {
            _calls++;
            return Task.FromResult(respond(_calls));
        }
    }

    /// <summary>A clock that stands still, so the entitlement period and the day budget are fixed.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
