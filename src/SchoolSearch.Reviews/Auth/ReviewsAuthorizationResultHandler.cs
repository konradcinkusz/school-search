using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Errors;

namespace SchoolSearch.Reviews.Auth;

/// <summary>
/// Maps policy outcomes to the uniform error shape: 503 auth_unavailable when authservice is not configured
/// (P8), 401 when no valid token is present, 403 email_not_verified for an unverified account, otherwise 403 forbidden.
/// Replaces the default empty challenge and forbid responses, which carry no error body.
/// </summary>
public sealed class ReviewsAuthorizationResultHandler(IOptions<AuthServiceOptions> options)
    : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        if (!options.Value.IsConfigured)
        {
            await ApiErrorWriter.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, "auth_unavailable", "Accounts are temporarily unavailable.");
            return;
        }

        if (authorizeResult.Challenged)
        {
            context.Response.Headers["WWW-Authenticate"] = "Bearer";
            await ApiErrorWriter.WriteAsync(context, StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
            return;
        }

        var needsVerifiedEmail = policy.Requirements.OfType<VerifiedEmailRequirement>().Any()
            && !AuthClaims.IsEmailVerified(context.User);

        if (needsVerifiedEmail)
        {
            await ApiErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "email_not_verified", "Confirm your e-mail address to do this.");
            return;
        }

        await ApiErrorWriter.WriteAsync(context, StatusCodes.Status403Forbidden, "forbidden", "You are not allowed to do this.");
    }
}
