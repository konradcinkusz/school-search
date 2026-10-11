using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.Filters;
using SchoolSearch.Api.RateLimiting;
using SchoolSearch.Api.Security;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// <c>POST /api/v1/ai/methodology-answer</c>. Body: <c>{"question": "..."}</c>, at most 300 characters. Failures use the
/// uniform error shape; the answer is plain text in a JSON string, never markup.
/// </summary>
public static class MethodologyQaEndpoints
{
    private const string RetryAfterSeconds = "3600";

    public static IEndpointRouteBuilder MapMethodologyQaEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").AddEndpointFilter<ValidationFilter>();
        api.MapPost("/ai/methodology-answer", AnswerMethodologyQuestion).WithName(MethodologyQaOperations.AnswerQuestion);

        return app;
    }

    internal static async Task<IResult> AnswerMethodologyQuestion(
        HttpContext httpContext,
        MethodologyQaService qa,
        ClientIpResolver clientIps,
        CancellationToken ct)
    {
        var (body, error) = await JsonBodyReader.ReadAsync<MethodologyAnswerRequest>(httpContext.Request, ct);
        if (error is not null)
        {
            return error;
        }

        var outcome = await qa.AnswerAsync(body?.Question ?? string.Empty, clientIps.Resolve(httpContext), ct);
        switch (outcome.Status)
        {
            case MethodologyQaStatus.Ok:
                return TypedResults.Ok(outcome.Response);
            case MethodologyQaStatus.InvalidQuestion:
                return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Wpisz pytanie o metodologię (od 1 do 300 znaków).");
            case MethodologyQaStatus.PersonalDataDetected:
                return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Nie wpisuj adresu, numeru telefonu ani danych dziecka.");
            case MethodologyQaStatus.Unavailable:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_unavailable", "Odpowiedź AI jest teraz niedostępna.");
            case MethodologyQaStatus.BudgetExhausted:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_budget_exhausted", "Dzienny limit pytań AI został wykorzystany.");
            case MethodologyQaStatus.RateLimited:
                httpContext.Response.Headers["Retry-After"] = RetryAfterSeconds;
                return ApiErrorWriter.Result(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Retry later.");
            case MethodologyQaStatus.OutputRejected:
                return ApiErrorWriter.Result(StatusCodes.Status502BadGateway, "ai_output_rejected", "Odpowiedź AI nie przeszła kontroli zgodności z dokumentem i nie została pokazana.");
            default:
                return ApiErrorWriter.Result(StatusCodes.Status500InternalServerError, "internal_error", "Unexpected server error.");
        }
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class MethodologyQaOperations
{
    public const string AnswerQuestion = "AnswerMethodologyQuestion";
}
