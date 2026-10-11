using System.Security.Cryptography;
using System.Text;

namespace SchoolSearch.Billing.Payments;

/// <summary>
/// HMAC-SHA256 over the raw body bytes, hex encoded. Verification is constant-time, and malformed signatures are
/// verification failures, never exceptions. <see cref="Sign"/> is used by the simulator to build a signed synthetic event.
/// </summary>
public static class WebhookSignature
{
    public static string Sign(string secret, string rawBody) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();

    public static bool Verify(string secret, string rawBody, string? presentedHex)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(presentedHex))
        {
            return false;
        }

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(presentedHex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody));

        // FixedTimeEquals returns false for different lengths, so a truncated signature fails without an exception.
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }

    /// <summary>
    /// The acceptance rule. With a secret, the signature must verify. Without one, mock mode accepts and any other
    /// mode rejects. A missing production secret therefore rejects every webhook, which is deliberate.
    /// </summary>
    public static bool Accepts(PaymentSettings settings, string rawBody, string? presentedHex) =>
        settings.WebhookSecret is { } secret
            ? Verify(secret, rawBody, presentedHex)
            : settings.IsMockMode;
}
