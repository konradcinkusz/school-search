using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Leads;

/// <summary>
/// Private access helpers for the Leads slice. Written here, not borrowed from F1, so that this slice compiles on
/// its own. Each rule mirrors a rule F1 owns and must stay in step with it:
/// the premium rule is EntitlementService.IsPremium (SPEC-F), the owner key is the subject hash (SHA-256 of pepper + sub).
/// </summary>
internal static class LeadAccess
{
    /// <summary>
    /// A school is premium when an approved claim exists, a subscription is active or canceling with a period end in the
    /// future, and the profile is not locked. Enquiries are open exactly when the school is premium.
    /// </summary>
    public static async Task<bool> IsPremiumAsync(BillingDbContext db, string slug, DateTimeOffset now, CancellationToken ct)
    {
        var profileOpen = await db.PremiumProfiles.AsNoTracking()
            .AnyAsync(p => p.SchoolSlug == slug && !p.IsLocked, ct);
        if (!profileOpen)
        {
            return false;
        }

        // The approved claim must belong to the owner of the subscription (same rule as EntitlementService).
        return await (
            from s in db.Subscriptions.AsNoTracking()
            join c in db.SchoolClaims.AsNoTracking() on s.OwnerHash equals c.OwnerHash
            where s.SchoolSlug == slug
                && c.SchoolSlug == slug
                && c.Status == LeadRules.ClaimApproved
                && (s.Status == LeadRules.SubscriptionActive || s.Status == LeadRules.SubscriptionCanceling)
                && s.CurrentPeriodEnd > now
            select s.Id).AnyAsync(ct);
    }

    /// <summary>The authservice subject, or null when the principal carries none.</summary>
    public static string? Subject(ClaimsPrincipal user)
    {
        var value = user.FindFirst(LeadRules.SubjectClaim)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Owner key: lowercase hex SHA-256 of (pepper + subject). Null when no pepper is configured.</summary>
    public static string? OwnerHash(IConfiguration configuration, string subject)
    {
        var pepper = configuration[LeadRules.SubjectPepperKey];
        if (string.IsNullOrWhiteSpace(pepper))
        {
            return null;
        }

        return Digest(pepper + subject);
    }

    /// <summary>True when the school has an approved claim held by this owner key.</summary>
    public static async Task<bool> IsOwnerAsync(BillingDbContext db, string slug, string ownerHash, CancellationToken ct) =>
        await db.SchoolClaims.AsNoTracking().AnyAsync(
            c => c.SchoolSlug == slug && c.OwnerHash == ownerHash && c.Status == LeadRules.ClaimApproved,
            ct);

    /// <summary>In-memory rate-limit partition key only. Never stored.</summary>
    public static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>
    /// The one place this slice resolves the client address. Honoured only with the BFF shared secret (constant-time
    /// compare), or Fly-Client-IP when the deployment says a proxy is in front. Otherwise the socket peer.
    /// </summary>
    public static string ClientIp(HttpContext context, IConfiguration configuration)
    {
        var secret = configuration[LeadRules.BffSharedSecretKey];
        if (!string.IsNullOrEmpty(secret)
            && context.Request.Headers.TryGetValue("X-Bff-Secret", out var presented)
            && context.Request.Headers.TryGetValue("X-Client-Ip", out var forwarded))
        {
            var expected = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(presented.ToString()));
            if (CryptographicOperations.FixedTimeEquals(expected, actual)
                && IPAddress.TryParse(forwarded.ToString().Trim(), out var parsed))
            {
                return parsed.ToString();
            }
        }

        var trustProxy = bool.TryParse(configuration[LeadRules.TrustProxyClientIpKey], out var trust) && trust;
        if (trustProxy && context.Request.Headers.TryGetValue("Fly-Client-IP", out var fly))
        {
            var value = fly.ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
