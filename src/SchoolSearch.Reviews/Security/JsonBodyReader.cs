using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SchoolSearch.Reviews.Errors;

namespace SchoolSearch.Reviews.Security;

/// <summary>
/// Reads a JSON body with the uniform error shape. Malformed JSON and oversize bodies become 400 / 413 responses.
/// Parser messages are never returned to the client.
/// </summary>
public static class JsonBodyReader
{
    public static async Task<(T? Value, IResult? Error)> ReadAsync<T>(HttpRequest request, CancellationToken ct)
        where T : class
    {
        try
        {
            var value = await JsonSerializer.DeserializeAsync<T>(request.Body, ApiJson.Options, ct);
            if (value is null)
            {
                return (null, ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required."));
            }

            return (value, null);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, ApiErrorWriter.Result(StatusCodes.Status413PayloadTooLarge, "payload_too_large", "Request body is too large."));
        }
        catch (BadHttpRequestException)
        {
            return (null, ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "invalid_body", "Request body is not valid."));
        }
        catch (JsonException)
        {
            return (null, ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "invalid_body", "Request body is not valid."));
        }
    }
}
