using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Security;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Billing.Ai;

/// <summary>The owner's draft: the text is returned to the owner only and is never saved or published.</summary>
public sealed record ProfileDraftResponse(string Headline, string Description, string AiLabel, string Model, bool Saved);

/// <summary>
/// <c>POST /api/v1/premium/{slug}/draft</c> (VerifiedUser; owner of an approved claim with a running subscription).
/// Failures use the uniform error shape. The AI text is returned as plain text in JSON strings, never as markup.
/// </summary>
public static class ProfileDraftEndpoints
{
    /// <summary>Name under which the feature is reported on /health (same name as the Reviews and Api features).</summary>
    public const string IntegrationName = "anthropic";

    /// <summary>The label every AI text carries (AI-FEATURES B6).</summary>
    public const string AiLabel = "Wygenerowano przez AI";

    private const string RetryAfterSeconds = "3600";
    private const int MaxSlugLength = 200;

    public static IEndpointRouteBuilder MapProfileDraftEndpoints(this IEndpointRouteBuilder app)
    {
        var availability = app.ServiceProvider.GetRequiredService<DraftAvailability>();
        app.ServiceProvider.GetRequiredService<IntegrationStatusRegistry>().Register(
            IntegrationName,
            availability.IsAvailable ? IntegrationState.Ok : IntegrationState.Disabled,
            availability.Detail);

        app.MapPost("/api/v1/premium/{slug}/draft", DraftProfile)
            .RequireAuthorization(AuthPolicies.VerifiedUser)
            .WithName(ProfileDraftOperations.DraftProfile);

        return app;
    }

    internal static async Task<IResult> DraftProfile(
        string slug,
        HttpRequest request,
        ClaimsPrincipal user,
        ProfileDraftService drafts,
        SubjectHasher hasher,
        IOptions<AiDraftOptions> options,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Length > MaxSlugLength)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Nieprawidłowy identyfikator szkoły.");
        }

        if (!hasher.IsConfigured)
        {
            return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "auth_unavailable", "Uwierzytelnianie jest teraz niedostępne.");
        }

        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<ProfileDraftRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var outcome = await drafts.DraftAsync(slug, hasher.Hash(subject), body, ct);
        switch (outcome.Status)
        {
            case ProfileDraftStatus.Drafted when outcome.Draft is { } draft:
                return TypedResults.Ok(new ProfileDraftResponse(
                    draft.Headline,
                    draft.Description,
                    AiLabel,
                    options.Value.Model,
                    Saved: false));
            case ProfileDraftStatus.Unavailable:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_unavailable", outcome.Message!);
            case ProfileDraftStatus.NotEntitled:
                return ApiErrorWriter.Result(StatusCodes.Status403Forbidden, "premium_required", outcome.Message!);
            case ProfileDraftStatus.InvalidInput:
                return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", outcome.Message!);
            case ProfileDraftStatus.ClaimNotAllowed:
                return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "claim_not_allowed", outcome.Message!);
            case ProfileDraftStatus.BudgetExhausted:
                return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "ai_budget_exhausted", outcome.Message!);
            case ProfileDraftStatus.RateLimited:
                request.HttpContext.Response.Headers["Retry-After"] = RetryAfterSeconds;
                return ApiErrorWriter.Result(StatusCodes.Status429TooManyRequests, "rate_limited", outcome.Message!);
            case ProfileDraftStatus.Rejected:
                return ApiErrorWriter.Result(StatusCodes.Status422UnprocessableEntity, "draft_rejected", outcome.Message!);
            default:
                return ApiErrorWriter.Result(StatusCodes.Status500InternalServerError, "internal_error", "Unexpected server error.");
        }
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class ProfileDraftOperations
{
    public const string DraftProfile = "DraftProfile";
}
