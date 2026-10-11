using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Api.Errors;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// <c>POST /api/v1/admin/corrections/{id}/triage</c>. Mapped INTO the admin route group, so every route of the group
/// (AdminAccessFilter: 404 without Auth:Authority, 401 without a valid token, 403 without Admin or SuperAdmin) applies
/// here too. The group is passed in; this file does not build its own.
/// </summary>
public static class CorrectionTriageEndpoints
{
    private const string RetryAfterSeconds = "3600";

    /// <summary>Adds the triage route to the admin group. Call once, right after the group is created.</summary>
    public static RouteGroupBuilder MapCorrectionTriageEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapPost("/corrections/{id:guid}/triage", TriageCorrection).WithName(CorrectionTriageOperations.Triage);

        return admin;
    }

    internal static async Task<IResult> TriageCorrection(
        Guid id,
        HttpContext httpContext,
        CorrectionTriageService triage,
        CancellationToken ct)
    {
        // The admin filter has already required a token with a subject; without one the request is refused, never shared.
        var subject = httpContext.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return ApiErrorWriter.Result(StatusCodes.Status403Forbidden, "forbidden", "Forbidden.");
        }

        var outcome = await triage.TriageAsync(id, subject, ct);
        switch (outcome.Status)
        {
            case CorrectionTriageStatus.Ok when outcome.Response is not null:
                return TypedResults.Ok(outcome.Response);
            case CorrectionTriageStatus.NotFound:
                return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Correction not found.");
            case CorrectionTriageStatus.Unavailable:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_unavailable", "Podpowiedź AI jest teraz niedostępna.");
            case CorrectionTriageStatus.BudgetExhausted:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_budget_exhausted", "Dzienny limit AI został wykorzystany.");
            case CorrectionTriageStatus.RateLimited:
                httpContext.Response.Headers["Retry-After"] = RetryAfterSeconds;
                return ApiErrorWriter.Result(StatusCodes.Status429TooManyRequests, "rate_limited", "Zbyt wiele żądań. Spróbuj później.");
            case CorrectionTriageStatus.InputRejected:
                return ApiErrorWriter.Result(
                    StatusCodes.Status422UnprocessableEntity,
                    "ai_input_rejected",
                    "Treść korekty zawiera dane kontaktowe lub numer identyfikacyjny, więc nie została wysłana do AI. Odpowiedz ręcznie.");
            case CorrectionTriageStatus.OutputRejected:
                return ApiErrorWriter.Result(
                    StatusCodes.Status502BadGateway,
                    "ai_output_rejected",
                    "Szkic AI nie przeszedł kontroli i nie został pokazany. Odpowiedz ręcznie.");
            default:
                return ApiErrorWriter.Result(StatusCodes.Status500InternalServerError, "internal_error", "Unexpected server error.");
        }
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class CorrectionTriageOperations
{
    public const string Triage = "AdminTriageCorrection";
}
