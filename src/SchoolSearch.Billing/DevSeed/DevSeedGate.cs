using System.Security.Cryptography;
using System.Text;

namespace SchoolSearch.Billing.DevSeed;

/// <summary>What the dev seed endpoint answers, decided before any body is read.</summary>
public enum DevSeedDecision
{
    /// <summary>Disabled, not configured, or Production: answer 404 as if the route did not exist.</summary>
    NotFound,

    /// <summary>Enabled, but the key header is missing or wrong: answer 401.</summary>
    Unauthorized,

    /// <summary>Enabled and the key matches: run the seed.</summary>
    Allowed,
}

/// <summary>
/// The access rule of <c>POST /dev/seed</c> (local and dev only). The endpoint exists only when DevSeed:Enabled is true,
/// DevSeed:Key is non-empty and the environment is not Production. The key is compared in constant time.
/// </summary>
public static class DevSeedGate
{
    public const string EnabledKey = "DevSeed:Enabled";
    public const string KeyKey = "DevSeed:Key";
    public const string HeaderName = "X-Dev-Seed-Key";

    /// <summary>Parses the raw configuration value of DevSeed:Enabled. Absent or unparsable means disabled.</summary>
    public static bool IsEnabled(string? configuredValue) =>
        bool.TryParse(configuredValue?.Trim(), out var enabled) && enabled;

    /// <summary>The decision for one request. Order: switch and environment first, then the key.</summary>
    public static DevSeedDecision Decide(bool enabled, string? configuredKey, bool isProduction, string? presentedKey)
    {
        if (!enabled || isProduction || string.IsNullOrWhiteSpace(configuredKey))
        {
            return DevSeedDecision.NotFound;
        }

        if (string.IsNullOrEmpty(presentedKey) || !KeysMatch(configuredKey, presentedKey))
        {
            return DevSeedDecision.Unauthorized;
        }

        return DevSeedDecision.Allowed;
    }

    /// <summary>
    /// Constant-time key comparison. Both sides are hashed first, so the comparison always runs over equal-length
    /// digests and the key length does not leak through an early exit.
    /// </summary>
    public static bool KeysMatch(string expected, string presented)
    {
        var expectedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(expectedDigest, presentedDigest);
    }
}
