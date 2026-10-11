using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Ai;
using SchoolSearch.Reviews.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Evals.Tests;

/// <summary>What one pipeline run did: the real <see cref="ReviewTriageService"/> with an in-memory database and the given model seam.</summary>
internal sealed record ReviewPipelineRun(
    ReviewTriageOutcome Outcome,
    bool ModelCalled,
    string? RawModelText,
    string StatusBefore,
    string StatusAfter,
    int TriageRows,
    int DecisionRows);

/// <summary>
/// Drives the production triage pipeline for one dataset case. Used offline (with <see cref="CannedReviewTriageClient"/>)
/// and live (with the real Anthropic client registered through its production DI path). Nothing here is a copy of the
/// service logic: the service decides the status, the budget, the window and the write.
/// </summary>
internal static class ReviewTriageHarness
{
    private static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly string DefaultAuthorHash = new('c', 64);

    public static async Task<ReviewPipelineRun> RunAsync(EvalCase c, IReviewTriageClient model, CancellationToken ct)
    {
        var condition = ReviewTriageEval.Condition(c);
        var (title, body) = ReviewTriageEval.ReviewText(c);

        // The daily budget is 0 for the budget condition; the per-minute allowance is high so that no case is rate limited.
        var settings = new AiTriageOptions
        {
            Enabled = true,
            DailyTokenBudget = condition == "budget_exhausted" ? 0 : 100_000,
            TriagesPerMinute = 1000,
        };
        var options = Options.Create(settings);
        var clock = new FixedClock(Start);
        var recorder = new RecordingReviewTriageClient(model);

        var available = condition != "no_key";
        var availability = new AiTriageAvailability(
            available,
            available ? "AI review triage is configured" : "ANTHROPIC_API_KEY is not set; AI review triage is disabled");

        using var db = CreateDb();
        var review = new Review
        {
            Id = Guid.NewGuid(),
            SchoolSlug = EvalJson.OptionalString(c.Input, "schoolSlug") ?? "szkola-syntetyczna-0001",
            AuthorHash = EvalJson.OptionalString(c.Input, "authorHash") ?? DefaultAuthorHash,
            Rating = 4,
            Title = title,
            Body = body,
            SchoolYearFrom = 2025,
            Status = ReviewStatuses.Pending,
            CreatedAt = Start,
            UpdatedAt = Start,
            PublishedAt = null,
            TermsVersion = "2026-01-01",
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);

        var service = new ReviewTriageService(
            db,
            recorder,
            new ReviewTriageTokenBudget(clock, options),
            new ReviewTriageRateLimiter(clock, options),
            availability,
            options,
            new IntegrationStatusRegistry(),
            clock,
            NullLogger<ReviewTriageService>.Instance);

        var outcome = await service.TriageAsync(review.Id, ct);

        var after = await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == review.Id, ct);
        var triageRows = await db.ModerationTriages.AsNoTracking().CountAsync(t => t.ReviewId == review.Id, ct);
        var decisionRows = await db.ModerationDecisions.AsNoTracking().CountAsync(d => d.ReviewId == review.Id, ct);

        return new ReviewPipelineRun(outcome, recorder.Calls > 0, recorder.LastText, review.Status, after.Status, triageRows, decisionRows);
    }

    private static ReviewsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseInMemoryDatabase("reviews-evals-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new ReviewsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}

/// <summary>Counts model calls and keeps the last answer. Forwards to the model seam it wraps.</summary>
internal sealed class RecordingReviewTriageClient(IReviewTriageClient inner) : IReviewTriageClient
{
    private int _calls;
    private string? _lastText;

    public int Calls => _calls;

    public string? LastText => _lastText;

    public async Task<ReviewTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        _calls++;
        var completion = await inner.CompleteAsync(system, user, ct);
        _lastText = completion.Text;
        return completion;
    }
}

/// <summary>
/// Offline model seam for one case: answers with the first acceptable label, an empty answer for the refusal condition,
/// or fails for the error condition. It is a fixture, not a judgement: it proves the dataset and the graders agree.
/// </summary>
internal sealed class CannedReviewTriageClient(EvalCase c) : IReviewTriageClient
{
    public Task<ReviewTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var condition = ReviewTriageEval.Condition(c);
        if (condition == "model_error")
        {
            return Task.FromException<ReviewTriageCompletion>(new InvalidOperationException("synthetic provider failure"));
        }

        if (condition == "model_refusal")
        {
            return Task.FromResult(new ReviewTriageCompletion(string.Empty, 50, 0));
        }

        var first = EvalJson.StringArray(c.Expect, "acceptable")[0];
        var text = JsonSerializer.Serialize(new { label = first, note = "offline canned answer" });
        return Task.FromResult(new ReviewTriageCompletion(text, 120, 30));
    }
}

/// <summary>A clock that does not move. The triage windows are not under test here.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
