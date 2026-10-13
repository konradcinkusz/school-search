using System;
using System.Globalization;
using System.Text.Json;

namespace SchoolSearch.Seeder;

/// <summary>
/// Reads the server's wait hint for a 429. The Retry-After header (whole seconds) wins; otherwise the body's
/// "retryAfter" number is used, because the authservice rate-limit response carries it there.
/// </summary>
public static class RetryAfterReader
{
    public static TimeSpan? Parse(string? headerValue, string body)
    {
        if (int.TryParse(headerValue?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("retryAfter", out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out double bodySeconds)
                && bodySeconds > 0)
            {
                return TimeSpan.FromSeconds(bodySeconds);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
