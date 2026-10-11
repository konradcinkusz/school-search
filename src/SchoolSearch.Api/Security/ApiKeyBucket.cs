namespace SchoolSearch.Api.Security;

/// <summary>
/// Per-request result of API key resolution, stored in <see cref="HttpContext.Items"/> so the
/// synchronous rate-limit partitioner can read it without touching the database.
/// Only the digest is carried, never the key.
/// </summary>
public sealed record ApiKeyBucket(string KeyHash)
{
    public const string ItemKey = "SchoolSearch.ApiKeyBucket";
}
