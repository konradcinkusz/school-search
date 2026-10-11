using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Corrections;
using SchoolSearch.Catalog.Data;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Moderator-only triage of a stored correction (AI-FEATURES feature 5). Order: availability (P8), correction and school
/// lookup (404), pre-send personal-data guard (422), cache (free), daily budget, per-moderator allowance, one model
/// call, strict parse and output guards, cache store. Nothing is written to the database and nothing is sent.
/// </summary>
public sealed class CorrectionTriageService(
    CatalogDbContext db,
    ICorrectionTriageClient client,
    IMemoryCache cache,
    TokenBudget budget,
    CorrectionTriageRateLimiter rateLimiter,
    AiAvailability availability,
    IOptions<AiOptions> options,
    ILogger<CorrectionTriageService> logger)
{
    /// <summary>How long a grounded triage is served from cache. Repeat views of the same correction are free.</summary>
    public const int CacheHours = 24;

    public async Task<CorrectionTriageOutcome> TriageAsync(Guid correctionId, string subjectKey, CancellationToken ct)
    {
        if (!availability.IsAvailable)
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.Unavailable);
        }

        var correction = await db.CorrectionRequests.AsNoTracking().FirstOrDefaultAsync(c => c.Id == correctionId, ct);
        if (correction is null)
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.NotFound);
        }

        var slug = await db.Schools.AsNoTracking()
            .Where(s => s.IdPodmiot == correction.SchoolId)
            .Select(s => s.Slug)
            .FirstOrDefaultAsync(ct);
        if (slug is null)
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.NotFound);
        }

        var input = new CorrectionTriageInput(slug, correction.Field, correction.Message);
        if (input.Note.Length > CorrectionRequestValidator.MaxMessageLength
            || CorrectionTriagePreSendGuard.ContainsPersonalData(input.Note))
        {
            // The text is never sent, not even in part: the moderator writes the reply by hand.
            return new CorrectionTriageOutcome(CorrectionTriageStatus.InputRejected);
        }

        var prompt = CorrectionTriagePromptBuilder.Build(input);
        var cacheKey = CacheKey(prompt.FactsJson, options.Value.Model);
        if (cache.TryGetValue<CorrectionTriageResponse>(cacheKey, out var cached) && cached is not null)
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.Ok, cached);
        }

        if (budget.IsExhausted())
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.BudgetExhausted);
        }

        if (!rateLimiter.TryAcquire(subjectKey))
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.RateLimited);
        }

        CorrectionTriageCompletion completion;
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
            logger.LogWarning("AI correction triage call failed with {ExceptionType}", ex.GetType().Name);
            return new CorrectionTriageOutcome(CorrectionTriageStatus.Unavailable);
        }

        budget.Record((long)completion.InputTokens + completion.OutputTokens);
        logger.LogInformation(
            "AI correction triage usage: input {InputTokens}, output {OutputTokens}",
            completion.InputTokens,
            completion.OutputTokens);

        var draft = CorrectionTriageParser.Parse(completion.Text, prompt.FactsJson);
        if (draft is null)
        {
            return new CorrectionTriageOutcome(CorrectionTriageStatus.OutputRejected);
        }

        var response = new CorrectionTriageResponse(
            draft.Category,
            draft.FieldLooksValid,
            draft.Severity,
            draft.SuggestedAction,
            draft.DraftReplyPl,
            ExplainService.AiLabel,
            options.Value.Model);

        cache.Set(
            cacheKey,
            response,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(CacheHours),
                Size = 1, // the shared cache has a SizeLimit; every entry must declare a size
            });

        return new CorrectionTriageOutcome(CorrectionTriageStatus.Ok, response);
    }

    /// <summary>Content hash of the exact facts, prompt version and model. The note itself is never a cache key in clear.</summary>
    internal static string CacheKey(string factsJson, string model)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(CorrectionTriagePromptBuilder.PromptVersion + "\n" + model + "\n" + factsJson));
        return "triage:" + Convert.ToHexString(digest);
    }
}
