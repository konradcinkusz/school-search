using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>The uniform error shape of the DSA endpoints: {"error":{"code","message","traceId"}}. Own copy, not the Api's.</summary>
public sealed record DsaErrorEnvelope(DsaErrorBody Error);

public sealed record DsaErrorBody(string Code, string Message, string TraceId);

/// <summary>Shared JSON options for DSA bodies (camelCase, case-insensitive input).</summary>
public static class DsaJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Error results, body reading and paging shared by the DSA endpoints.</summary>
public static class DsaHttp
{
    public static IResult Error(int status, string code, string message) =>
        Results.Json(
            new DsaErrorEnvelope(new DsaErrorBody(code, message, Activity.Current?.TraceId.ToString() ?? string.Empty)),
            DsaJson.Options,
            statusCode: status);

    public static IResult Failure(DsaFailure failure) => Error(failure.Status, failure.Code, failure.Message);

    /// <summary>Reads the JSON body. A malformed body is 400 invalid_json; an absent body is 400 validation_failed.</summary>
    public static async Task<(T? Body, IResult? Error)> ReadBodyAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        try
        {
            var body = await request.ReadFromJsonAsync<T>(DsaJson.Options, ct);
            if (body is null)
            {
                return (null, Error(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required."));
            }

            return (body, null);
        }
        catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
        {
            return (null, Error(StatusCodes.Status400BadRequest, "invalid_json", "Request body is not valid JSON."));
        }
    }

    /// <summary>Page and page size with the same clamping as the Api: page at least 1 and at most 10 000, size 1 to 100.</summary>
    public static (int Page, int Size) ClampPaging(string? page, string? pageSize)
    {
        var pageNumber = int.TryParse(page, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var p)
            ? Math.Clamp(p, 1, 10_000)
            : 1;
        var size = int.TryParse(pageSize, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, 1, 100)
            : 20;
        return (pageNumber, size);
    }
}
