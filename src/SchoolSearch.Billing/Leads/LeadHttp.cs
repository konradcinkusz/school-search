using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace SchoolSearch.Billing.Leads;

/// <summary>Body reading, error results and the receipt shared by the Leads endpoints.</summary>
internal static class LeadHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IResult Problem(int status, string code, string message) =>
        Results.Json(
            new LeadErrorEnvelope(new LeadErrorBody(code, message, Activity.Current?.TraceId.ToString() ?? string.Empty)),
            JsonOptions,
            statusCode: status);

    /// <summary>202 with no identifier: a parent never learns whether a lead was stored or the request was a honeypot hit.</summary>
    public static IResult Received() =>
        Results.Json(new EnquiryReceipt("received"), JsonOptions, statusCode: StatusCodes.Status202Accepted);

    /// <summary>Reads the JSON body. Oversize is 413, malformed is 400 invalid_json, absent is 400 validation_failed.</summary>
    public static async Task<(T? Body, IResult? Error)> ReadBodyAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        try
        {
            var body = await request.ReadFromJsonAsync<T>(JsonOptions, ct);
            if (body is null)
            {
                return (null, Problem(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required."));
            }

            return (body, null);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, Problem(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "Request body is too large."));
        }
        catch (Exception ex) when (ex is JsonException or BadHttpRequestException)
        {
            // Parser messages can echo submitted text, so they are dropped rather than returned.
            return (null, Problem(StatusCodes.Status400BadRequest, "invalid_json", "Request body is not valid JSON."));
        }
    }
}
