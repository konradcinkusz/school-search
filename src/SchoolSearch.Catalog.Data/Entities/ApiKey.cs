namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// A public API key. Only the SHA-256 hex digest is stored; the plain key is shown once at creation.
/// Label is a free-text note for operators and must hold no personal data.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; set; }

    /// <summary>SHA-256 hex digest of the plain key. Unique.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>Operator-facing label. Max 80 characters, no personal data.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Rate-limit tier. "free" in Phase C.</summary>
    public string Tier { get; set; } = "free";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the key is revoked. Null while the key is active.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
