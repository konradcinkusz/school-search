using System;
using System.Text.Json;

namespace SchoolSearch.Seeder;

/// <summary>
/// Outcome of one HTTP call. <see cref="NoAnswer"/> (status 0) means no HTTP answer: network error or timeout.
/// The generated ToString is replaced so that a body (which may carry a token) is never logged by accident.
/// </summary>
public sealed record ApiResponse(int StatusCode, string Body, TimeSpan? RetryAfter)
{
    public const int NoAnswer = 0;

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Worth another attempt: no answer, rate limited (429) or temporarily unavailable (503).</summary>
    public bool IsTransient => StatusCode is NoAnswer or 429 or 503;

    public static ApiResponse NoResponse() => new(NoAnswer, string.Empty, null);

    /// <summary>The body as a JSON object, or null when it is not one. The returned element owns its data.</summary>
    public JsonElement? Json()
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return null;
        }

        try
        {
            JsonElement element = JsonSerializer.Deserialize<JsonElement>(Body);
            return element.ValueKind == JsonValueKind.Object ? element : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public override string ToString() => StatusCode == NoAnswer ? "no HTTP answer" : $"HTTP {StatusCode}";
}
