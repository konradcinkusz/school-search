using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Reviews.Ai;

/// <summary>Result of one triage attempt. Only <see cref="Triaged"/> carries a label.</summary>
public enum ReviewTriageStatus
{
    Triaged,
    Unavailable,
    NotFound,
    NotPending,
    AlreadyTriaged,
    RateLimited,
    BudgetExhausted,
    Failed,
}

public sealed record ReviewTriageOutcome(ReviewTriageStatus Status, string? Label = null);

/// <summary>
/// AI moderation triage (advisory). The pipeline, in order: availability, review lookup, pending check, existing label,
/// input size, daily budget, per-minute allowance, prompt, one model call, defensive parse, one ModerationTriage row.
/// It writes only <see cref="ModerationTriage"/>. It never touches <see cref="Review.Status"/>, never creates a
/// ModerationDecision and never sets Automated. Only the moderation endpoints publish, reject or remove a review.
/// </summary>
public sealed class ReviewTriageService(
    ReviewsDbContext db,
    IReviewTriageClient client,
    ReviewTriageTokenBudget budget,
    ReviewTriageRateLimiter rateLimiter,
    AiTriageAvailability availability,
    IOptions<AiTriageOptions> options,
    IntegrationStatusRegistry integrations,
    TimeProvider clock,
    ILogger<ReviewTriageService> logger)
{
    /// <summary>Name under which the feature is reported on /health (same name as the Api's explain feature).</summary>
    public const string IntegrationName = "anthropic";

    /// <summary>Only pending reviews are triaged: the moderator queue shows them.</summary>
    public const string PendingStatus = "pending";

    /// <summary>Reports the configured state on /health. Called once by the worker when it starts.</summary>
    public static void Announce(IntegrationStatusRegistry registry, AiTriageAvailability availability) =>
        registry.Register(
            IntegrationName,
            availability.IsAvailable ? IntegrationState.Ok : IntegrationState.Disabled,
            availability.Detail);

    public async Task<ReviewTriageOutcome> TriageAsync(Guid reviewId, CancellationToken ct)
    {
        // Degrade first (P8): with no key or the kill switch off, nothing is read and no model call is possible.
        if (!availability.IsAvailable)
        {
            return new ReviewTriageOutcome(ReviewTriageStatus.Unavailable);
        }

        var review = await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return new ReviewTriageOutcome(ReviewTriageStatus.NotFound);
        }

        if (review.Status != PendingStatus)
        {
            return new ReviewTriageOutcome(ReviewTriageStatus.NotPending);
        }

        if (await db.ModerationTriages.AsNoTracking().AnyAsync(t => t.ReviewId == reviewId, ct))
        {
            return new ReviewTriageOutcome(ReviewTriageStatus.AlreadyTriaged);
        }

        var settings = options.Value;
        if (review.Title.Length + review.Body.Length > settings.MaxInputChars)
        {
            // Not truncated: a cut-off text could hide the very name the moderator needs to see.
            return await SaveAsync(reviewId, new ReviewTriageResult(ReviewTriageLabels.Unclear, "input longer than the triage limit"), settings.Model, ct);
        }

        if (budget.IsExhausted())
        {
            return new ReviewTriageOutcome(ReviewTriageStatus.BudgetExhausted);
        }

        if (!rateLimiter.TryAcquire())
        {
            return new ReviewTriageOutcome(ReviewTriageStatus.RateLimited);
        }

        var prompt = ReviewTriagePromptBuilder.Build(new ReviewTriageInput(review.Title, review.Body));

        ReviewTriageCompletion completion;
        try
        {
            completion = await client.CompleteAsync(prompt.System, prompt.User, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The exception type only: SDK messages can echo request details, and the key must never reach a log.
            logger.LogWarning("AI review triage call failed with {ExceptionType}", ex.GetType().Name);
            integrations.Register(IntegrationName, IntegrationState.Degraded, "last call failed; see server logs");
            return new ReviewTriageOutcome(ReviewTriageStatus.Failed);
        }

        budget.Record((long)completion.InputTokens + completion.OutputTokens);
        integrations.Register(IntegrationName, IntegrationState.Ok, "AI review triage configured");
        logger.LogInformation(
            "AI review triage usage: input {InputTokens}, output {OutputTokens}",
            completion.InputTokens,
            completion.OutputTokens);

        return await SaveAsync(reviewId, ReviewTriageParser.Parse(completion.Text), settings.Model, ct);
    }

    private async Task<ReviewTriageOutcome> SaveAsync(Guid reviewId, ReviewTriageResult result, string model, CancellationToken ct)
    {
        db.ModerationTriages.Add(new ModerationTriage
        {
            ReviewId = reviewId,
            Label = result.Label,
            Note = result.Note,
            Model = model,
            At = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another triage of the same review won the race for the primary key. The label already exists.
            db.ChangeTracker.Clear();
            return new ReviewTriageOutcome(ReviewTriageStatus.AlreadyTriaged);
        }

        return new ReviewTriageOutcome(ReviewTriageStatus.Triaged, result.Label);
    }
}
