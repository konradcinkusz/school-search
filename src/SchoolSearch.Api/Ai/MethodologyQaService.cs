using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Scoring;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Methodology Q&amp;A (AI-FEATURES feature 7). The pipeline, in order: availability (P8), pre-send guard, cache (free),
/// daily budget, per-client allowance, one model call, defensive parse with citation check, digit grounding against the
/// cited sections, cache store. The model sees the methodology document and the question only; no school data.
/// </summary>
public sealed partial class MethodologyQaService(
    MethodologyDocument document,
    IMethodologyQaClient client,
    IMemoryCache cache,
    TokenBudget budget,
    MethodologyQaRateLimiter rateLimiter,
    AiAvailability availability,
    IOptions<AiOptions> aiOptions,
    IOptions<MethodologyQaOptions> options,
    ILogger<MethodologyQaService> logger)
{
    public const string AiLabel = "Wygenerowano przez AI";
    public const int MaxQuestionChars = 300;

    public async Task<MethodologyQaOutcome> AnswerAsync(string question, string clientKey, CancellationToken ct)
    {
        // Degrade first (P8): with no key or the kill switch off, nothing else runs and no SDK call is possible.
        if (!availability.IsAvailable)
        {
            return new MethodologyQaOutcome(MethodologyQaStatus.Unavailable);
        }

        var normalized = Normalize(question);
        if (normalized.Length == 0 || question.Length > MaxQuestionChars)
        {
            return new MethodologyQaOutcome(MethodologyQaStatus.InvalidQuestion);
        }

        if (MethodologyQaGuard.ContainsPersonalData(question))
        {
            return new MethodologyQaOutcome(MethodologyQaStatus.PersonalDataDetected);
        }

        var cacheKey = CacheKey(normalized);
        var cached = cache.Get<MethodologyAnswerResponse>(cacheKey);
        if (cached is not null)
        {
            return new MethodologyQaOutcome(MethodologyQaStatus.Ok, cached);
        }

        if (budget.IsExhausted())
        {
            return new MethodologyQaOutcome(MethodologyQaStatus.BudgetExhausted);
        }

        if (!rateLimiter.TryAcquire(clientKey))
        {
            return new MethodologyQaOutcome(MethodologyQaStatus.RateLimited);
        }

        var prompt = MethodologyQaPromptBuilder.Build(document, question);

        MethodologyQaCompletion completion;
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
            logger.LogWarning("Methodology Q&A call failed with {ExceptionType}", ex.GetType().Name);
            return new MethodologyQaOutcome(MethodologyQaStatus.Unavailable);
        }

        budget.Record((long)completion.InputTokens + completion.OutputTokens);
        logger.LogInformation(
            "Methodology Q&A usage: input {InputTokens}, output {OutputTokens}",
            completion.InputTokens,
            completion.OutputTokens);

        var draft = MethodologyQaParser.Parse(completion.Text, document);
        if (draft is null || (draft.Covered && !NumberGuard.IsGrounded(draft.Answer, document.SectionsText(draft.CitedSections))))
        {
            // Invalid JSON, an unknown citation or a number the document does not contain: nothing is shown.
            return new MethodologyQaOutcome(MethodologyQaStatus.OutputRejected);
        }

        var response = new MethodologyAnswerResponse(
            draft.Answer,
            draft.CitedSections,
            AiLabel,
            aiOptions.Value.Model,
            GroundedInDocument: true,
            MethodologyVersion: ScoringPolicy.MethodologyVersion);

        cache.Set(
            cacheKey,
            response,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours)),
                Size = 1, // the shared cache has a SizeLimit; every entry must declare a size
            });

        return new MethodologyQaOutcome(MethodologyQaStatus.Ok, response);
    }

    /// <summary>Lower case, trimmed, whitespace collapsed. Two spellings of one question share one cache entry.</summary>
    internal static string Normalize(string question) =>
        WhitespaceRun().Replace(question.Trim(), " ").ToLowerInvariant();

    /// <summary>Key per normalised question and methodology version: a new methodology never reuses old text.</summary>
    internal static string CacheKey(string normalizedQuestion) =>
        $"methodology-qa:{ScoringPolicy.MethodologyVersion}:{normalizedQuestion}";

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();
}
