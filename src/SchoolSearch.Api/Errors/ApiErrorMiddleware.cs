using Microsoft.AspNetCore.Http;

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
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", SafeForLog(context.Request.Method), SafeForLog(context.Request.Path.Value));
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

    // The path is attacker-controlled: drop control characters so a request cannot forge log lines.
    private static string SafeForLog(string? value) =>
        string.Concat((value ?? string.Empty).Where(c => !char.IsControl(c)));
}
