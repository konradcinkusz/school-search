using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.Models;
using SchoolSearch.Api.RateLimiting;
using SchoolSearch.Api.Security;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// <c>POST /api/v1/schools/{slug}/explain</c>. The body is optional: empty, or <c>{"lang":"pl"}</c>.
/// Failures use the uniform error shape; the AI text is returned as plain text in a JSON string, never as markup.
/// </summary>
public static class AiEndpoints
{
    /// <summary>Name under which the feature is reported on /health.</summary>
    public const string IntegrationName = "anthropic";

    private const string RetryAfterSeconds = "3600";

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        var availability = app.ServiceProvider.GetRequiredService<AiAvailability>();
        app.ServiceProvider.GetRequiredService<IntegrationStatusRegistry>().Register(
            IntegrationName,
            availability.IsAvailable ? IntegrationState.Ok : IntegrationState.Disabled,
            availability.Detail);

        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();
        api.MapPost("/schools/{slug}/explain", ExplainSchool).WithName(AiOperations.ExplainSchool);

        return app;
    }

    internal static async Task<IResult> ExplainSchool(
        [AsParameters] SchoolSlugRequest request,
        HttpContext httpContext,
        ExplainService explain,
        ClientIpResolver clientIps,
        CancellationToken ct)
    {
        var (body, error) = await ReadBodyAsync(httpContext.Request, ct);
        if (error is not null)
        {
            return error;
        }

        var lang = (body?.Lang ?? ExplainService.LanguagePolish).Trim().ToLowerInvariant();
        if (lang != ExplainService.LanguagePolish)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Parametr lang przyjmuje wyłącznie wartość pl.");
        }

        var outcome = await explain.ExplainAsync(request.Slug, lang, clientIps.Resolve(httpContext), ct);
        switch (outcome.Status)
        {
            case ExplainStatus.Ok:
                return TypedResults.Ok(outcome.Response);
            case ExplainStatus.NotFound:
                return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Nie znaleziono szkoły.");
            case ExplainStatus.Unavailable:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_unavailable", "Wyjaśnienie AI jest teraz niedostępne.");
            case ExplainStatus.BudgetExhausted:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_budget_exhausted", "Dzienny limit wyjaśnień AI został wykorzystany.");
            case ExplainStatus.RateLimited:
                httpContext.Response.Headers["Retry-After"] = RetryAfterSeconds;
                return ApiErrorWriter.Result(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Retry later.");
            case ExplainStatus.OutputRejected:
                return ApiErrorWriter.Result(StatusCodes.Status502BadGateway, "ai_output_rejected", "Odpowiedź AI nie przeszła kontroli zgodności z danymi i nie została pokazana.");
            default:
                return ApiErrorWriter.Result(StatusCodes.Status500InternalServerError, "internal_error", "Unexpected server error.");
        }
    }

    /// <summary>An empty body (no Content-Length) means the defaults; anything else must be a valid JSON object.</summary>
    private static async Task<(ExplainRequest? Body, IResult? Error)> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength is null or 0)
        {
            return (new ExplainRequest(null), null);
        }

        var (body, error) = await JsonBodyReader.ReadAsync<ExplainRequest>(request, ct);
        return (body, error);
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class AiOperations
{
    public const string ExplainSchool = "ExplainSchool";
}
