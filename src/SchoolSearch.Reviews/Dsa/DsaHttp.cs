using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SchoolSearch.Reviews.Errors;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>Body reading, error results and paging shared by the DSA endpoints. Errors use the service's uniform envelope.</summary>
public static class DsaHttp
{
    /// <summary>Request bodies are read camelCase and case-insensitive (web defaults).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Error(int status, string code, string message) => ApiErrorWriter.Result(status, code, message);

    public static IResult Failure(DsaFailure failure) => Error(failure.Status, failure.Code, failure.Message);

    /// <summary>Reads the JSON body. A malformed body is 400 invalid_json; an absent body is 400 validation_failed.</summary>
    public static async Task<(T? Body, IResult? Error)> ReadBodyAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        try
        {
            var body = await request.ReadFromJsonAsync<T>(JsonOptions, ct);
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

    /// <summary>Page at least 1 and at most 10 000; size 1 to 100, default 20 (the same bounds as the Api).</summary>
    public static (int Page, int Size) ClampPaging(string? page, string? pageSize)
    {
        var pageNumber = int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
            ? Math.Clamp(p, 1, 10_000)
            : 1;
        var size = int.TryParse(pageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, 1, 100)
            : 20;
        return (pageNumber, size);
    }
}
