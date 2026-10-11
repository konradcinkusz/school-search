using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Subscriptions;

/// <summary>
/// The single place that decides whether a school has premium (ADR-0016, brief §1e). Premium holds only when ALL of:
/// an approved claim exists for the school and belongs to the subscription's owner; the subscription Status is
/// active or canceling; CurrentPeriodEnd is after now; and the premium profile is not locked.
/// Nothing else grants or revokes premium: not the price, not the success redirect, not a client flag.
/// Ranking, score and search order never read this service.
/// </summary>
public sealed class EntitlementService(BillingDbContext db)
{
    /// <summary>True when the school currently has premium under the rule above.</summary>
    public async Task<bool> IsPremiumAsync(string schoolSlug, DateTimeOffset now, CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolSlug == schoolSlug, ct);

        if (subscription is null)
        {
            return false;
        }

        var paid = subscription.Status == SubscriptionStatuses.Active
                   || subscription.Status == SubscriptionStatuses.Canceling;
        var periodEnd = subscription.CurrentPeriodEnd;
        if (!paid || periodEnd is null || periodEnd <= now)
        {
            return false;
        }

        var ownerHash = subscription.OwnerHash;
        var approvedByOwner = await db.SchoolClaims.AsNoTracking().AnyAsync(
            c => c.SchoolSlug == schoolSlug
                 && c.OwnerHash == ownerHash
                 && c.Status == ClaimStatuses.Approved,
            ct);

        if (!approvedByOwner)
        {
            return false;
        }

        var locked = await db.PremiumProfiles.AsNoTracking()
            .AnyAsync(p => p.SchoolSlug == schoolSlug && p.IsLocked, ct);

        return !locked;
    }

    /// <summary>
    /// True when a sponsored profile may be shown and enquired about: premium holds AND a profile row exists.
    /// Enquiries (F3) are open exactly when this is true and the school's enquiries are not closed by the owner.
    /// </summary>
    public async Task<bool> IsPublishedAsync(string schoolSlug, DateTimeOffset now, CancellationToken ct = default)
    {
        if (!await IsPremiumAsync(schoolSlug, now, ct))
        {
            return false;
        }

        return await db.PremiumProfiles.AsNoTracking().AnyAsync(p => p.SchoolSlug == schoolSlug, ct);
    }

    /// <summary>
    /// True when a moderator has approved a claim for the school ("Zweryfikowane przez redakcję"). Subscription state is
    /// not considered: the statement is about the representative's claim, which a moderator decides.
    /// </summary>
    public Task<bool> HasApprovedClaimAsync(string schoolSlug, CancellationToken ct = default) =>
        db.SchoolClaims.AsNoTracking().AnyAsync(
            c => c.SchoolSlug == schoolSlug && c.Status == ClaimStatuses.Approved,
            ct);
}
