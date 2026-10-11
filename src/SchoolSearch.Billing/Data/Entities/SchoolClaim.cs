namespace SchoolSearch.Billing.Data;

/// <summary>
/// A self-declared representative's claim to speak for a school. Approval is a manual check by a moderator outside the
/// system; only an approved claim makes a profile publishable and only "approved" may be shown as verified by the editors.
/// One row per (school, owner): a rejected claim is re-opened in place.
/// </summary>
public sealed class SchoolClaim
{
    public Guid Id { get; set; }

    /// <summary>Slug of the school. No foreign key: the Api service owns schools (database per service).</summary>
    public string SchoolSlug { get; set; } = "";

    /// <summary>Hex SHA-256 of (pepper + authservice sub), 64 characters. No raw sub and no e-mail address is stored.</summary>
    public string OwnerHash { get; set; } = "";

    /// <summary>Self-declared role, up to 120 characters, for example "Dyrekcja".</summary>
    public string RoleDescription { get; set; } = "";

    /// <summary>One of <see cref="ClaimStatuses"/>.</summary>
    public string Status { get; set; } = ClaimStatuses.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Hash of the deciding moderator's subject.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Moderator-internal note, up to 500 characters. Never shown to the claimant or publicly.</summary>
    public string? Notes { get; set; }
}
