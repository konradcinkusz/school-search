using Microsoft.AspNetCore.Builder;

namespace SchoolSearch.Mcp.Security;

/// <summary>
/// Headers for a JSON and event-stream API that serves no HTML: nothing may be loaded, framed or referred from it.
/// </summary>
public static class McpSecurityHeaders
{
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public static IApplicationBuilder UseMcpSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return next();
        });
    }
}
