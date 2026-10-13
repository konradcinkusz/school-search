using System;
using System.Text.Json;

namespace SchoolSearch.Seeder;

/// <summary>
/// Reads the "sub" claim from a JWT payload. The signature is not checked: the seeder only needs the user id that
/// authservice issued, and the token never leaves the process.
/// </summary>
public static class JwtSubject
{
    /// <summary>The subject, or null when the token is malformed or has no non-empty string "sub" claim.</summary>
    public static string? TryRead(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = DecodeBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sub", out JsonElement sub))
            {
                return null;
            }

            if (sub.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? value = sub.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Base64url without padding (RFC 7515) to bytes. Throws <see cref="FormatException"/> on bad input.</summary>
    public static byte[] DecodeBase64Url(string input)
    {
        string standard = input.Replace('-', '+').Replace('_', '/');
        int padding = (4 - (standard.Length % 4)) % 4;
        return Convert.FromBase64String(standard + new string('=', padding));
    }
}
