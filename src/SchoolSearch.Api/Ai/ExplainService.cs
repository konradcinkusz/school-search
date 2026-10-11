using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Scoring;
using SchoolSearch.Api.Search;
using SchoolSearch.Api.Services;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// "Wyjaśnij ten wynik" (AI-FEATURES feature 1). The pipeline, in order: availability, school lookup (404), cache
/// (free), daily budget, per-client allowance, profile and prompt, one model call, output guard, cache store.
/// The prompt is built only from the profile the Api computes for the school; no user text reaches the model.
/// </summary>
public sealed class ExplainService(
    CatalogDbContext db,
    SchoolScoreService scores,
    IExplainClient client,
    IMemoryCache cache,
    TokenBudget budget,
    ExplainRateLimiter rateLimiter,
    AiAvailability availability,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<ExplainService> logger)
{
    public const string LanguagePolish = "pl";
    public const string AiLabel = "Wygenerowano przez AI";

    public async Task<ExplainOutcome> ExplainAsync(string slug, string lang, string clientKey, CancellationToken ct)
    {
        // Degrade first (P8): with no key or the kill switch off, nothing else is looked up and no SDK call is possible.
        if (!availability.IsAvailable)
        {
            return new ExplainOutcome(ExplainStatus.Unavailable);
        }

        var school = await db.Schools.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug, ct);
        if (school is null)
        {
            return new ExplainOutcome(ExplainStatus.NotFound);
        }

        var year = await scores.ResolveCurrentYearAsync(ct);
        var cacheKey = CacheKey(slug, year, lang);
        var cached = cache.Get<ExplainResponse>(cacheKey);
        if (cached is not null)
        {
            return new ExplainOutcome(ExplainStatus.Ok, cached with { Cached = true });
        }

        if (budget.IsExhausted())
        {
            return new ExplainOutcome(ExplainStatus.BudgetExhausted);
        }

        if (!rateLimiter.TryAcquire(clientKey))
        {
            return new ExplainOutcome(ExplainStatus.RateLimited);
        }

        var profile = await SchoolReadModel.BuildProfileAsync(db, scores, school, year, ct);
        var facts = ExplainFacts.From(profile, year);
        var prompt = ExplainPromptBuilder.Build(facts);

        ExplainCompletion completion;
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
            logger.LogWarning("AI explain call failed with {ExceptionType}", ex.GetType().Name);
            return new ExplainOutcome(ExplainStatus.Unavailable);
        }

        var tokens = (long)completion.InputTokens + completion.OutputTokens;
        budget.Record(tokens);
        logger.LogInformation(
            "AI explain usage: input {InputTokens}, output {OutputTokens}",
            completion.InputTokens,
            completion.OutputTokens);

        var text = ExplainPromptBuilder.Finish(completion.Text);
        if (text.Length == 0 || !NumberGuard.IsGrounded(text, prompt.FactsJson))
        {
            // Never publish a number the Api did not compute: the reader sees the structured facts instead.
            return new ExplainOutcome(ExplainStatus.OutputRejected);
        }

        var response = new ExplainResponse(
            slug,
            text,
            AiLabel,
            options.Value.Model,
            profile.MethodologyVersion,
            year,
            facts,
            clock.GetUtcNow(),
            Cached: false);

        cache.Set(
            cacheKey,
            response,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours)),
                Size = 1, // the shared cache has a SizeLimit; every entry must declare a size
            });

        return new ExplainOutcome(ExplainStatus.Ok, response);
    }

    /// <summary>Key per school, methodology, exam year and language. A new methodology or exam year never reuses old text.</summary>
    internal static string CacheKey(string slug, int? year, string lang) =>
        $"explain:{slug}:{ScoringPolicy.MethodologyVersion}:{(year?.ToString() ?? "none")}:{lang}";
}
