using System.Security.Cryptography;
using System.Text;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>Unguessable tokens: 16 bytes from the CSPRNG, base64url without padding (22 characters).</summary>
public static class DsaTokens
{
    public const int Length = 22;

    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Constant-time comparison of a stored token with a presented one.</summary>
    public static bool Matches(string expected, string presented)
    {
        if (expected.Length == 0 || presented.Length != expected.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }
}
