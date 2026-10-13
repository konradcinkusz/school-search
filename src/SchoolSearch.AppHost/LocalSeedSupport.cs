// Generators for the local demo seed (Phase L) used by Program.cs. Local and dev only.
// Generated values are kept only in the gitignored local store (LocalSeedStore.cs); they are never logged or committed.
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;

internal static class LocalSeedSupport
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+";
    private const string AllCharacters = Upper + Lower + Digits + Symbols;

    // Null for a missing or blank configuration value, so "unset" is decided in one place.
    public static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public static string OrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    // RS256 signing key for authservice: RSA 2048, PKCS#8 PEM, on one line with literal \n escapes, the format authservice expects.
    public static string CreateJwtPrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem().Trim();
        return pem.Replace("\r\n", "\n").Replace("\n", "\\n");
    }

    // Random: 32 bytes as 64 hex characters. Keys the dev seed endpoints of reviews and billing.
    public static string CreateDevSeedKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // Random pepper for the subject hashes (32 bytes as 64 hex characters), used when no pepper is configured or stored.
    public static string CreatePepper() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // Random test password: at least one upper case letter, one lower case letter, one digit and one symbol, shuffled.
    // The symbols are a conservative set that needs no shell or URL escaping.
    public static string CreatePassword(int length = 24)
    {
        var chars = new char[length];
        chars[0] = Pick(Upper);
        chars[1] = Pick(Lower);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < length; i++)
        {
            chars[i] = Pick(AllCharacters);
        }

        // Fisher-Yates shuffle, so the guaranteed classes do not sit at fixed positions.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    // Passes the AI settings through to a resource. Only called when ANTHROPIC_API_KEY is configured (P8).
    public static IResourceBuilder<ProjectResource> WithAiSettings(
        IResourceBuilder<ProjectResource> resource,
        string anthropicApiKey,
        string? aiEnabled)
    {
        var result = resource.WithEnvironment("ANTHROPIC_API_KEY", anthropicApiKey);
        return aiEnabled is null ? result : result.WithEnvironment("AI_ENABLED", aiEnabled);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
