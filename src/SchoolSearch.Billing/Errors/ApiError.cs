using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SchoolSearch.Billing.Errors;

/// <summary>The single error shape every client sees: {"error":{"code","message","traceId"}}. Copied from the Api, not referenced.</summary>
public sealed record ApiErrorEnvelope(ApiErrorBody Error);

public sealed record ApiErrorBody(string Code, string Message, string TraceId);

public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

public static class ApiErrorWriter
{
    public static Task WriteAsync(HttpContext context, int status, string code, string message, CancellationToken ct = default)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Envelope(code, message), ApiJson.Options, ct);
    }

    public static IResult Result(int status, string code, string message) =>
        Results.Json(Envelope(code, message), ApiJson.Options, statusCode: status);

    private static ApiErrorEnvelope Envelope(string code, string message) =>
        new(new ApiErrorBody(code, message, Activity.Current?.TraceId.ToString() ?? string.Empty));
}
