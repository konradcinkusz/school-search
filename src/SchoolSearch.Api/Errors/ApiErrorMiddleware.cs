using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SchoolSearch.Api.Errors;

/// <summary>
/// Turns unhandled exceptions and bare routing failures into the uniform error shape.
/// Exception messages are logged server-side only and never returned to the client.
/// </summary>
public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log the matched route template (server-defined), never the raw request path or method.
            var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
            logger.LogError(ex, "Unhandled exception for route {Route} (trace {TraceId})", route, context.TraceIdentifier);
            if (!context.Response.HasStarted)
            {
                await ApiErrorWriter.WriteAsync(context, StatusCodes.Status500InternalServerError, "internal_error", "Unexpected server error.");
            }
            return;
        }

        // Unmatched routes and wrong verbs leave no endpoint and no body; give them the same shape.
        if (!context.Response.HasStarted && context.GetEndpoint() is null)
        {
            switch (context.Response.StatusCode)
            {
                case StatusCodes.Status404NotFound:
                    await ApiErrorWriter.WriteAsync(context, 404, "not_found", "Resource not found.");
                    break;
                case StatusCodes.Status405MethodNotAllowed:
                    await ApiErrorWriter.WriteAsync(context, 405, "method_not_allowed", "Method not allowed.");
                    break;
            }
        }
    }
}
