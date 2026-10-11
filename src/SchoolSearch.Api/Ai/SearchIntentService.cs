using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// "Doprecyzuj wyszukiwanie" (AI-FEATURES feature 2). The pipeline, in order: pre-send guard (400), availability (503),
/// cache (free), daily budget (503), per-client allowance (429), one model call, strict parse (502), cache store.
/// The answer carries filters only. No school, score or rank is read or returned, and the model's prose is never shown.
/// </summary>
public sealed class SearchIntentService(
    ISearchIntentClient client,
    IMemoryCache cache,
    TokenBudget budget,
    SearchIntentRateLimiter rateLimiter,
    AiAvailability availability,
    IOptions<AiOptions> aiOptions,
    IOptions<SearchIntentOptions> intentOptions,
    ILogger<SearchIntentService> logger)
{
    public const string AiLabel = "Wygenerowano przez AI";

    public async Task<SearchIntentOutcome> InterpretAsync(string? question, string clientKey, CancellationToken ct)
    {
        var problem = SearchIntentGuard.Validate(question);
        if (problem is not null)
        {
            return new SearchIntentOutcome(SearchIntentStatus.InvalidInput, Message: problem);
        }

        var text = question!.Trim();

        // Degrade first (P8): with no key or the kill switch off, nothing is looked up and no model call is possible.
        if (!availability.IsAvailable)
        {
            return new SearchIntentOutcome(SearchIntentStatus.Unavailable);
        }

        var model = aiOptions.Value.Model;
        var cacheKey = CacheKey(SearchIntentGuard.Normalize(text), model);
        var cached = cache.Get<SearchIntentResponse>(cacheKey);
        if (cached is not null)
        {
            return new SearchIntentOutcome(SearchIntentStatus.Ok, cached);
        }

        if (budget.IsExhausted())
        {
            return new SearchIntentOutcome(SearchIntentStatus.BudgetExhausted);
        }

        if (!rateLimiter.TryAcquire(clientKey))
        {
            return new SearchIntentOutcome(SearchIntentStatus.RateLimited);
        }

        var prompt = SearchIntentPromptBuilder.Build(text);

        SearchIntentCompletion completion;
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
            logger.LogWarning("AI search-intent call failed with {ExceptionType}", ex.GetType().Name);
            return new SearchIntentOutcome(SearchIntentStatus.Unavailable);
        }

        var tokens = (long)completion.InputTokens + completion.OutputTokens;
        budget.Record(tokens);
        logger.LogInformation(
            "AI search-intent usage: input {InputTokens}, output {OutputTokens}",
            completion.InputTokens,
            completion.OutputTokens);

        var parsed = SearchIntentParser.Parse(completion.Text, text);
        if (parsed is null)
        {
            // Any answer that is not a clean object is dropped whole: no partial trust in a model's JSON.
            return new SearchIntentOutcome(SearchIntentStatus.OutputRejected);
        }

        var response = new SearchIntentResponse(parsed.Filters, AiLabel, model, parsed.Unsupported);

        cache.Set(
            cacheKey,
            response,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, aiOptions.Value.CacheHours)),
                Size = 1, // the shared cache has a SizeLimit; every entry must declare a size
            });

        return new SearchIntentOutcome(SearchIntentStatus.Ok, response);
    }

    /// <summary>Key per normalised question and model. Only the normalised question text is kept, and only in memory.</summary>
    internal static string CacheKey(string normalizedQuestion, string model) =>
        $"searchintent:{model}:{normalizedQuestion}";
}
