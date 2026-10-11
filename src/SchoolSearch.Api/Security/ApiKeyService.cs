using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Issues and resolves public API keys. The plain key is returned once at issue time and never stored:
/// only its SHA-256 hex digest is persisted, and lookups compare digests. A key that is unknown,
/// malformed or revoked resolves to null, which callers treat as anonymous (not as an error).
/// </summary>
public sealed class ApiKeyService(CatalogDbContext db, TimeProvider clock)
{
    /// <summary>Visible prefix so that leaked keys can be recognised by secret scanners.</summary>
    public const string Prefix = "ss_";

    private const int KeyEntropyBytes = 32;
    private const int MinKeyLength = 10;
    private const int MaxKeyLength = 128;

    /// <summary>Creates a key with 256 bits from the CSPRNG and returns it in plain form. Persists the digest only.</summary>
    public async Task<string> IssueAsync(string label, CancellationToken ct)
    {
        var plain = GenerateKey();

        db.ApiKeys.Add(new ApiKey
        {
            Id = Guid.NewGuid(),
            KeyHash = HashKey(plain),
            Label = label,
            Tier = "free",
            CreatedAt = clock.GetUtcNow(),
        });

        await db.SaveChangesAsync(ct);
        return plain;
    }

    /// <summary>
    /// Returns the digest of the presented key when it names an active (not revoked) key, otherwise null.
    /// The digest, not the key, is what rate limiting partitions on.
    /// </summary>
    public async Task<string?> ResolveActiveKeyHashAsync(string? presented, CancellationToken ct)
    {
        if (!IsWellFormed(presented))
        {
            return null;
        }

        var hash = HashKey(presented);
        var active = await db.ApiKeys
            .AsNoTracking()
            .AnyAsync(k => k.KeyHash == hash && k.RevokedAt == null, ct);

        return active ? hash : null;
    }

    /// <summary>Lower-case SHA-256 hex of the UTF-8 key. Keys carry 256 random bits, so the digest is a safe lookup key.</summary>
    public static string HashKey(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(KeyEntropyBytes);
        // Base64url without padding, so the key is safe in headers and URLs.
        var body = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return Prefix + body;
    }

    private static bool IsWellFormed(string? presented) =>
        presented is not null
        && presented.Length is >= MinKeyLength and <= MaxKeyLength
        && presented.StartsWith(Prefix, StringComparison.Ordinal);
}
