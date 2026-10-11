using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Subscriptions;

/// <summary>
/// Lock and unlock of a premium profile. Expiry locks ("subscription_expired"); a confirmed renewal unlocks
/// (the webhook handlers in Payments/ call <see cref="Unlock"/>). A lock hides nothing permanently and deletes nothing.
/// </summary>
public static class ProfileLocks
{
    public const string SubscriptionExpiredReason = "subscription_expired";

    public static void Lock(PremiumProfile profile, string reason, DateTimeOffset now)
    {
        profile.IsLocked = true;
        profile.LockedReason = reason;
        profile.LockedAt = now;
        profile.UpdatedAt = now;
    }

    public static void Unlock(PremiumProfile profile, DateTimeOffset now)
    {
        profile.IsLocked = false;
        profile.LockedReason = null;
        profile.LockedAt = null;
        profile.UpdatedAt = now;
    }
}
