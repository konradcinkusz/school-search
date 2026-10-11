using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.RateLimiting;
using SchoolSearch.Api.Security;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// <c>POST /api/v1/ai/search-intent</c>: a parent's question becomes filters the parent can edit. Failures use the
/// uniform error shape. Health is reported by <see cref="AiEndpoints"/> (integration "anthropic"), not repeated here.
/// </summary>
public static class SearchIntentEndpoints
{
    private const string RetryAfterSeconds = "3600";

    public static IEndpointRouteBuilder MapSearchIntentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();
        api.MapPost("/ai/search-intent", InterpretSearchIntent).WithName(SearchIntentOperations.Interpret);

        return app;
    }

    internal static async Task<IResult> InterpretSearchIntent(
        HttpContext httpContext,
        SearchIntentService intent,
        ClientIpResolver clientIps,
        CancellationToken ct)
    {
        var (body, error) = await JsonBodyReader.ReadAsync<SearchIntentRequest>(httpContext.Request, ct);
        if (error is not null)
        {
            return error;
        }

        if (body is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var outcome = await intent.InterpretAsync(body.Question, clientIps.Resolve(httpContext), ct);
        switch (outcome.Status)
        {
            case SearchIntentStatus.Ok:
                return TypedResults.Ok(outcome.Response);
            case SearchIntentStatus.InvalidInput:
                return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", outcome.Message ?? SearchIntentGuard.EmptyMessage);
            case SearchIntentStatus.Unavailable:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_unavailable", "Wyszukiwanie językowe jest niedostępne. Użyj filtrów.");
            case SearchIntentStatus.BudgetExhausted:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_budget_exhausted", "Dzienny limit wyszukiwania AI został wykorzystany. Użyj filtrów.");
            case SearchIntentStatus.RateLimited:
                httpContext.Response.Headers["Retry-After"] = RetryAfterSeconds;
                return ApiErrorWriter.Result(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Retry later.");
            case SearchIntentStatus.OutputRejected:
                return ApiErrorWriter.Result(StatusCodes.Status502BadGateway, "ai_output_rejected", "Nie udało się bezpiecznie zinterpretować pytania. Użyj filtrów.");
            default:
                return ApiErrorWriter.Result(StatusCodes.Status500InternalServerError, "internal_error", "Unexpected server error.");
        }
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class SearchIntentOperations
{
    public const string Interpret = "InterpretSearchIntent";
}
