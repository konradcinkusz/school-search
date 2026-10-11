using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Ai;

/// <summary>Result of one draft request. Only <see cref="ProfileDraftStatus.Drafted"/> carries text, and that text is never saved.</summary>
public enum ProfileDraftStatus
{
    Drafted,
    Unavailable,
    NotEntitled,
    InvalidInput,
    ClaimNotAllowed,
    BudgetExhausted,
    RateLimited,
    Rejected,
}

/// <param name="Status">What happened.</param>
/// <param name="Draft">The proposed headline and description, only when <see cref="Status"/> is Drafted.</param>
/// <param name="Message">A Polish message for the owner, for every status except Drafted.</param>
public sealed record ProfileDraftOutcome(ProfileDraftStatus Status, DraftCandidate? Draft = null, string? Message = null);

/// <summary>
/// "Zaproponuj tekst" (F6). The pipeline, in order: availability, owner entitlement, input shape, claim lint on the
/// facts, daily token budget, per-owner allowance, then up to <see cref="MaxAttempts"/> model calls. A candidate is
/// returned only when it parses, stays inside the profile limits, passes the claim linter and grounds every digit in
/// the facts. Nothing is written: the owner edits the text and saves it through the profile PUT.
/// </summary>
public sealed class ProfileDraftService(
    BillingDbContext db,
    IDraftClient client,
    DraftTokenBudget budget,
    DraftRateLimiter rateLimiter,
    DraftAvailability availability,
    TimeProvider clock,
    ILogger<ProfileDraftService> logger)
{
    /// <summary>One call plus one retry, as the spec allows. A second failure is answered with draft_rejected.</summary>
    public const int MaxAttempts = 2;

    public async Task<ProfileDraftOutcome> DraftAsync(
        string schoolSlug,
        string ownerHash,
        ProfileDraftRequest request,
        CancellationToken ct)
    {
        // Degrade first (P8): with no key or the kill switch off, nothing is read and no model call is possible.
        if (!availability.IsAvailable)
        {
            return Failure(ProfileDraftStatus.Unavailable, "Asystent AI jest teraz niedostępny.");
        }

        if (!await DraftAccess.IsEntitledAsync(db, schoolSlug, ownerHash, clock.GetUtcNow(), ct))
        {
            return Failure(ProfileDraftStatus.NotEntitled, "Propozycja tekstu wymaga zatwierdzonej szkoły i aktywnej subskrypcji.");
        }

        var (bullets, inputError) = DraftInputValidator.Validate(request.Bullets);
        if (bullets is null)
        {
            return Failure(ProfileDraftStatus.InvalidInput, inputError ?? "Nieprawidłowe punkty z faktami.");
        }

        // A banned claim in the facts would only be echoed back; refuse it before any model call (same rule as the PUT).
        if (DraftChecks.FindClaimInFacts(bullets) is { } factsTerm)
        {
            return Failure(ProfileDraftStatus.ClaimNotAllowed, $"Punkty zawierają sformułowanie niedozwolone w profilu: {factsTerm}.");
        }

        if (budget.IsExhausted())
        {
            return Failure(ProfileDraftStatus.BudgetExhausted, "Dzienny limit propozycji AI został wykorzystany.");
        }

        if (!rateLimiter.TryAcquire(ownerHash))
        {
            return Failure(ProfileDraftStatus.RateLimited, "Too many requests. Retry later.");
        }

        var prompt = DraftPromptBuilder.Build(bullets);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (budget.IsExhausted())
            {
                return Failure(ProfileDraftStatus.BudgetExhausted, "Dzienny limit propozycji AI został wykorzystany.");
            }

            DraftCompletion completion;
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
                logger.LogWarning("AI profile draft call failed with {ExceptionType}", ex.GetType().Name);
                return Failure(ProfileDraftStatus.Unavailable, "Asystent AI jest teraz niedostępny.");
            }

            budget.Record((long)completion.InputTokens + completion.OutputTokens);
            logger.LogInformation(
                "AI profile draft usage: input {InputTokens}, output {OutputTokens}",
                completion.InputTokens,
                completion.OutputTokens);

            var candidate = DraftParser.Parse(completion.Text);
            if (candidate is null)
            {
                logger.LogInformation("AI profile draft attempt {Attempt} was not usable JSON", attempt);
                continue;
            }

            var failure = DraftChecks.Evaluate(candidate, bullets);
            if (failure is null)
            {
                return new ProfileDraftOutcome(ProfileDraftStatus.Drafted, candidate);
            }

            logger.LogInformation("AI profile draft attempt {Attempt} failed check {Check}", attempt, failure.Code);
        }

        return Failure(
            ProfileDraftStatus.Rejected,
            "Propozycja nie przeszła kontroli treści i nie została pokazana. Napisz tekst ręcznie lub spróbuj ponownie.");
    }

    private static ProfileDraftOutcome Failure(ProfileDraftStatus status, string message) => new(status, null, message);
}
