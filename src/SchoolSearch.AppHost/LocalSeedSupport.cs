// Generators for the local demo seed (Phase L) used by Program.cs. Local and dev only.
// Generated values are kept only in the gitignored local store (LocalSeedStore.cs); they are never logged or committed.
using System.Security.Cryptography;
using System.Text;
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

    // Minimum size of SESSION_SECRET in bytes (web/app/src/lib/bff-config.ts, MIN_SESSION_SECRET_BYTES). A shorter value
    // makes every account route of the web BFF answer 503.
    public const int SessionSecretMinimumBytes = 32;

    // Random key material for the sealed session cookie: 48 random bytes as 64 base64 characters (the same recipe as
    // `openssl rand -base64 48` in flyio/SECRETS.md).
    public static string CreateSessionSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    // Random secret shared by the web BFF and the services (BFF_SHARED_SECRET): 32 random bytes as 64 hex characters.
    public static string CreateBffSharedSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    // True when the value is long enough to be used as SESSION_SECRET. Pure; used for configured and stored values.
    public static bool IsUsableSessionSecret(string? value) =>
        value is not null && Encoding.UTF8.GetByteCount(value) >= SessionSecretMinimumBytes;

    // Minimum length of the authservice password rules (RequiredLength = 8).
    public const int AuthservicePasswordMinimumLength = 8;

    // Pure check of the authservice Identity password options (src/AuthService/Program.cs, AddIdentity in the
    // authservice repository): RequiredLength 8, RequireDigit, RequireLowercase, RequireUppercase and
    // RequireNonAlphanumeric. RequiredUniqueChars keeps the ASP.NET default of 1, so it adds no rule. Used by
    // CreatePassword as a guard, so a generated value that would be refused by InitialAdmin never reaches the store.
    public static bool MeetsAuthservicePasswordRules(string? password)
    {
        if (password is null || password.Length < AuthservicePasswordMinimumLength || password.Length > 100)
        {
            return false;
        }

        var hasUpper = false;
        var hasLower = false;
        var hasDigit = false;
        var hasNonAlphanumeric = false;
        foreach (var c in password)
        {
            if (c >= 'A' && c <= 'Z')
            {
                hasUpper = true;
            }
            else if (c >= 'a' && c <= 'z')
            {
                hasLower = true;
            }
            else if (c >= '0' && c <= '9')
            {
                hasDigit = true;
            }
            else
            {
                hasNonAlphanumeric = true;
            }
        }

        return hasUpper && hasLower && hasDigit && hasNonAlphanumeric;
    }

    // Random test password: at least one upper case letter, one lower case letter, one digit and one symbol, shuffled.
    // The symbols are a conservative set that needs no shell or URL escaping, and none of them needs JSON or
    // environment-variable escaping (no quote, backslash, space or control character). Checked against
    // MeetsAuthservicePasswordRules before it is returned.
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

        var password = new string(chars);
        if (!MeetsAuthservicePasswordRules(password))
        {
            throw new InvalidOperationException("The generated test password does not meet the authservice password rules.");
        }

        return password;
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
