using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Ai;

/// <summary>
/// Who may ask for a draft: the owner of an approved claim on the school who also has a paid subscription that is
/// running (active, or canceling but not yet ended). The owner is the subject hash, as stored on both rows. Expiry and
/// locks are not re-checked here: a draft is never published, so a lapsed owner gains nothing beyond the text.
/// The status strings are the values the Billing data model writes (see Data/).
/// </summary>
internal static class DraftAccess
{
    public const string ApprovedClaimStatus = "approved";
    public const string ActiveSubscriptionStatus = "active";
    public const string CancelingSubscriptionStatus = "canceling";

    public static async Task<bool> IsEntitledAsync(
        BillingDbContext db,
        string schoolSlug,
        string ownerHash,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var approved = await db.SchoolClaims.AsNoTracking().AnyAsync(
            claim => claim.SchoolSlug == schoolSlug
                && claim.OwnerHash == ownerHash
                && claim.Status == ApprovedClaimStatus,
            ct);
        if (!approved)
        {
            return false;
        }

        return await db.Subscriptions.AsNoTracking().AnyAsync(
            subscription => subscription.SchoolSlug == schoolSlug
                && subscription.OwnerHash == ownerHash
                && (subscription.Status == ActiveSubscriptionStatus || subscription.Status == CancelingSubscriptionStatus)
                && subscription.CurrentPeriodEnd > now,
            ct);
    }
}
