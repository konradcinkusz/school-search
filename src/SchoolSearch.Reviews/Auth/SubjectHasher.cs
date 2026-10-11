using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Settings;

namespace SchoolSearch.Reviews.Auth;

/// <summary>
/// Turns an authservice subject into the author key stored with reviews: hex SHA-256 of (pepper + sub).
/// The pepper lives outside the database, so the stored hashes cannot be joined back to authservice without it.
/// Outside Production with no pepper configured, an ephemeral per-process pepper is used (hashes change on restart).
/// In Production with no pepper, <see cref="IsConfigured"/> is false and review writes answer 503.
/// </summary>
public sealed class SubjectHasher
{
    private readonly string _pepper;

    public SubjectHasher(IOptions<ReviewsOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.SubjectPepper;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            _pepper = configured;
            IsConfigured = true;
        }
        else if (environment.IsProduction())
        {
            _pepper = string.Empty;
            IsConfigured = false;
        }
        else
        {
            _pepper = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            IsConfigured = true;
            IsEphemeral = true;
        }
    }

    /// <summary>False only in Production without Reviews:SubjectPepper.</summary>
    public bool IsConfigured { get; }

    /// <summary>True when the pepper was generated for this process (non-production without configuration).</summary>
    public bool IsEphemeral { get; }

    /// <summary>Returns the 64-character lowercase hex digest for an authservice subject.</summary>
    public string Hash(string subject)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var bytes = Encoding.UTF8.GetBytes(_pepper + subject);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
