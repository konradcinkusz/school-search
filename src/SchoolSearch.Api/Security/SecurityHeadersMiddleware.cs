using Microsoft.AspNetCore.Http;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Headers for a JSON-only API. The API serves no HTML, so the CSP forbids every source and framing.
/// Admin and key-issuance responses are never cached: the issued key is shown once.
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
        && (path.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/keys", StringComparison.OrdinalIgnoreCase));
}
