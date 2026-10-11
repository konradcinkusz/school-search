using System.Security.Cryptography;
using System.Text;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>
/// Pseudonymous subject key: lowercase hex SHA-256 of (pepper + sub). It is the same formula as the review
/// authorship hash, so an author or a moderator can be matched to the rows they own without storing the sub.
/// Without the server-side pepper (Reviews:SubjectPepper) the stored values cannot be joined back to authservice.
/// </summary>
public sealed class DsaSubjectHasher
{
    private readonly string? _pepper;

    public DsaSubjectHasher(string? pepper) =>
        _pepper = string.IsNullOrWhiteSpace(pepper) ? null : pepper;

    public bool IsConfigured => _pepper is not null;

    public string Hash(string subject)
    {
        if (_pepper is null)
        {
            throw new InvalidOperationException("Reviews:SubjectPepper is not configured.");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_pepper + subject))).ToLowerInvariant();
    }
}
