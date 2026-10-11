using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.Auth;

/// <summary>
/// Turns an authservice subject into the owner key stored with claims, subscriptions and profiles: hex SHA-256 of
/// (pepper + sub). The pepper lives outside the database, so stored hashes cannot be joined back to authservice without it.
/// Outside Production with no pepper configured, an ephemeral per-process pepper is used (hashes change on restart).
/// In Production with no pepper, <see cref="IsConfigured"/> is false and owner writes answer 503.
/// </summary>
public sealed class SubjectHasher
{
    private readonly string _pepper;

    public SubjectHasher(IOptions<BillingOptions> options, IHostEnvironment environment)
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

    /// <summary>False only in Production without Billing:SubjectPepper.</summary>
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
