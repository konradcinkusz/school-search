using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Api.Errors;

namespace SchoolSearch.Api.Security;

/// <summary>Public key issuance: POST /api/v1/keys. Shares the per-IP window, plus a strict issuance policy.</summary>
public static class ApiKeyEndpoints
{
    public const string KeyIssuePolicy = "api-key-issue";
    public const int MaxLabelLength = 80;

    public static IEndpointRouteBuilder MapKeyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/keys", IssueKey)
            .WithName("IssueApiKey")
            .RequireRateLimiting(KeyIssuePolicy);

        return app;
    }

    internal static async Task<IResult> IssueKey(HttpRequest request, ApiKeyService keys, CancellationToken ct)
    {
        var (body, error) = await JsonBodyReader.ReadAsync<CreateKeyRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var label = body.Label?.Trim();
        if (!IsValidLabel(label))
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                $"Label must be 1 to {MaxLabelLength} characters with no control characters.");
        }

        var plain = await keys.IssueAsync(label, ct);

        // The plain key is in this response only. It is not logged and not stored.
        return Results.Json(new IssuedApiKey(plain), ApiJson.Options, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>1 to 80 characters after trimming, with no control characters (tabs, newlines, NUL and the like).</summary>
    internal static bool IsValidLabel([NotNullWhen(true)] string? label) =>
        !string.IsNullOrEmpty(label)
        && label.Length <= MaxLabelLength
        && !label.Any(char.IsControl);
}

public sealed class CreateKeyRequest
{
    public string? Label { get; set; }
}

public sealed record IssuedApiKey(string ApiKey);
