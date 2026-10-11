using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using SchoolSearch.Api.Errors;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Caps request bodies at 16 KB for methods that carry one. Declared Content-Length is refused up front;
/// chunked bodies are cut off by Kestrel through the per-request limit. A body-size BadHttpRequestException
/// that escapes an endpoint is converted here, so it is not reported as an unhandled 500.
/// </summary>
public sealed class RequestBodyLimitMiddleware(RequestDelegate next)
{
    public const long MaxBodyBytes = 16 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!CarriesBody(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength is > MaxBodyBytes)
        {
            await ApiErrorWriter.WriteAsync(context, StatusCodes.Status413PayloadTooLarge, "payload_too_large", "Request body is too large.");
            return;
        }

        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit is not null && !limit.IsReadOnly)
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
        {
            var status = ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;
            var code = status == StatusCodes.Status413PayloadTooLarge ? "payload_too_large" : "invalid_body";
            var message = status == StatusCodes.Status413PayloadTooLarge ? "Request body is too large." : "Request body is not valid.";
            await ApiErrorWriter.WriteAsync(context, status, code, message);
        }
    }

    private static bool CarriesBody(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method);
}
