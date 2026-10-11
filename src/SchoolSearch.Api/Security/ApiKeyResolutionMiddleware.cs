using Microsoft.AspNetCore.Http;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Reads X-Api-Key and records the digest of an active key on the request. Invalid, unknown or revoked
/// keys leave the request anonymous; they are never an error. Runs before the rate limiter.
/// No key, digest or header value is logged.
/// </summary>
public sealed class ApiKeyResolutionMiddleware(RequestDelegate next, ILogger<ApiKeyResolutionMiddleware> logger)
{
    public const string HeaderName = "X-Api-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var presented = header.ToString().Trim();
            try
            {
                var service = context.RequestServices.GetRequiredService<ApiKeyService>();
                var hash = await service.ResolveActiveKeyHashAsync(presented, context.RequestAborted);
                if (hash is not null)
                {
                    context.Items[ApiKeyBucket.ItemKey] = new ApiKeyBucket(hash);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fail to the anonymous bucket rather than fail the request; the exception text is not logged
                // with the header, only the fact of the failure.
                logger.LogWarning("API key lookup failed; request is metered as anonymous ({ExceptionType}).", ex.GetType().Name);
            }
        }

        await next(context);
    }
}
