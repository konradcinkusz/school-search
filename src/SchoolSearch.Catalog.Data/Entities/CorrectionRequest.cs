namespace SchoolSearch.Catalog.Data.Entities;

/// <summary>
/// A public correction request about a school's published data. The submitter receives
/// <see cref="PublicToken"/> (random, url-safe, unguessable) to check the status; the
/// internal <see cref="Id"/> is never exposed.
/// </summary>
public sealed class CorrectionRequest
{
    public Guid Id { get; set; }

    /// <summary>Url-safe random token, at least 22 characters. Unique.</summary>
    public string PublicToken { get; set; } = string.Empty;

    public long SchoolId { get; set; }

    /// <summary>Name of the data field the correction concerns.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>Submitter's message. Never echoed by the status endpoint.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional contact address. Never echoed by the status endpoint.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>One of "new", "in_review", "accepted", "rejected", "duplicate".</summary>
    public string Status { get; set; } = "new";

    /// <summary>Reason given by the reviewer for the current status, when any.</summary>
    public string? StatusReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
