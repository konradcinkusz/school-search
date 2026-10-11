using Microsoft.AspNetCore.Http;

namespace SchoolSearch.Billing.Security;

/// <summary>
/// Headers for a JSON-only API. The service serves no HTML, so the CSP forbids every source and framing.
/// Responses about one person (their claims, subscriptions, profile drafts, enquiries, moderation) are never cached.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = ApiContentSecurityPolicy;

        if (IsSensitive(context.Request.Path.Value))
        {
            headers["Cache-Control"] = "no-store";
        }

        return next(context);
    }

    private static bool IsSensitive(string? path) =>
        path is not null
        && (path.StartsWith("/api/v1/claims", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/moderation", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/subscriptions", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/billing", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/enquiries", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/enquiries", StringComparison.OrdinalIgnoreCase));
}
