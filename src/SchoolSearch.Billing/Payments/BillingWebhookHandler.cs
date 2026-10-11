using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Payments;

public enum WebhookStatus
{
    Accepted,
    Duplicate,
    InvalidSignature,
    InvalidPayload,
}

/// <summary>Result of one delivery. Outcome is the stored WebhookOutcomes value; null for duplicates and rejections.</summary>
public sealed record WebhookResult(WebhookStatus Status, string? EventId, string? Outcome, string? Message);

/// <summary>
/// The single place where a provider event becomes entitlement. The real webhook endpoint and the mock simulator both call
/// <see cref="ProcessAsync"/>, so the simulator exercises verification, mapping, idempotency and the lifecycle rules.
/// Idempotency is keyed on the provider event id (WebhookEvent primary key). All writes for one event commit together
/// with its WebhookEvent row, so a failed delivery leaves nothing behind and the provider's retry applies it once.
/// </summary>
public sealed class BillingWebhookHandler(
    BillingDbContext db,
    PaymentSettings settings,
    IBillingEventMapper mapper,
    TimeProvider clock,
    ILogger<BillingWebhookHandler> logger)
{
    /// <summary>Lock reason set by expiry. Only a renewal clears it, so other locks (for example moderation) survive a payment.</summary>
    private const string SubscriptionExpiredReason = "subscription_expired";

    public async Task<WebhookResult> ProcessAsync(string rawBody, string? signature, CancellationToken ct)
    {
        if (!WebhookSignature.Accepts(settings, rawBody, signature))
        {
            logger.LogWarning("Billing webhook rejected: signature missing or invalid");
            return new WebhookResult(WebhookStatus.InvalidSignature, null, null, "Webhook signature is missing or invalid.");
        }

        BillingEvent evt;
        try
        {
            evt = mapper.Map(rawBody);
        }
        catch (BillingPayloadException ex)
        {
            logger.LogWarning("Billing webhook rejected: {Reason}", ex.Message);
            return new WebhookResult(WebhookStatus.InvalidPayload, null, null, ex.Message);
        }

        return await HandleAsync(evt, ct);
    }

    internal async Task<WebhookResult> HandleAsync(BillingEvent evt, CancellationToken ct)
    {
        if (await AlreadyRecordedAsync(evt.EventId, ct))
        {
            logger.LogInformation("Duplicate billing event {EventId} acknowledged without effect", evt.EventId);
            return new WebhookResult(WebhookStatus.Duplicate, evt.EventId, null, null);
        }

        var now = clock.GetUtcNow();
        var outcome = await ApplyAsync(evt, now, ct);

        db.WebhookEvents.Add(new WebhookEvent
        {
            EventId = evt.EventId,
            Type = evt.RawType,
            ReceivedAt = now,
            Outcome = outcome,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent delivery of the same event id won the insert race. Its writes stand; this delivery has no effect.
            if (await AlreadyRecordedAsync(evt.EventId, ct))
            {
                logger.LogInformation("Billing event {EventId} lost an insert race to a duplicate delivery", evt.EventId);
                return new WebhookResult(WebhookStatus.Duplicate, evt.EventId, null, null);
            }

            throw;
        }

        logger.LogInformation("Billing event {EventId} ({EventType}) recorded with outcome {Outcome}", evt.EventId, evt.RawType, outcome);
        return new WebhookResult(WebhookStatus.Accepted, evt.EventId, outcome, null);
    }

    private async Task<string> ApplyAsync(BillingEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        switch (evt.Kind)
        {
            case BillingEventKind.Unknown:
                // Explicit default branch. It is logged and recorded, never silently dropped.
                logger.LogWarning("Unmapped billing event type {EventType} recorded without effect", evt.RawType);
                return WebhookOutcomes.UnknownType;

            case BillingEventKind.RenewalFailed:
            case BillingEventKind.SubscriptionSuspended:
                // Explicit no-op. The paid period runs to its end, and expiry is decided by the expiration service.
                logger.LogWarning("Billing event {EventType} recorded without entitlement change", evt.RawType);
                return WebhookOutcomes.Ignored;
        }

        var subscription = await FindSubscriptionAsync(evt, ct);
        if (subscription is null)
        {
            var slug = evt.SchoolSlug;
            var owner = evt.OwnerHash;
            var plan = evt.PlanCode;
            if (evt.Kind != BillingEventKind.PaymentSucceeded || slug is null || owner is null || plan is null)
            {
                logger.LogWarning("Billing event {EventType} has no matching subscription and cannot create one", evt.RawType);
                return WebhookOutcomes.Ignored;
            }

            // The webhook beat the checkout write. Synthesise the row from the payload instead of dropping the payment.
            subscription = new Subscription
            {
                Id = evt.SubscriptionId ?? Guid.NewGuid(),
                SchoolSlug = slug,
                OwnerHash = owner,
                PlanCode = plan,
                Status = SubscriptionStatuses.Pending,
                CancelAtPeriodEnd = false,
                ProviderRef = evt.ProviderSubscriptionRef,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Subscriptions.Add(subscription);
            await ApplyPaymentAsync(subscription, evt, now, ct);
            return WebhookOutcomes.OrderSynthesised;
        }

        switch (evt.Kind)
        {
            case BillingEventKind.PaymentSucceeded:
                await ApplyPaymentAsync(subscription, evt, now, ct);
                return WebhookOutcomes.Applied;

            case BillingEventKind.SubscriptionCancelled:
                ApplyCancel(subscription, now);
                return WebhookOutcomes.Applied;

            case BillingEventKind.SubscriptionEnded:
                await ApplyEndedAsync(subscription, now, ct);
                return WebhookOutcomes.Applied;

            default:
                return WebhookOutcomes.Ignored;
        }
    }

    private async Task ApplyPaymentAsync(Subscription subscription, BillingEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        subscription.PlanCode = evt.PlanCode ?? subscription.PlanCode;
        subscription.CurrentPeriodEnd = PeriodMath.NewPeriodEnd(
            subscription.CurrentPeriodEnd,
            evt.OccurredAt,
            evt.PeriodEnd,
            settings.IntervalFor(subscription.PlanCode));
        subscription.Status = subscription.CancelAtPeriodEnd ? SubscriptionStatuses.Canceling : SubscriptionStatuses.Active;
        subscription.UpdatedAt = now;

        // A renewal unlocks a profile that expiry locked. Any other lock is left in place.
        var profile = await db.PremiumProfiles.FirstOrDefaultAsync(p => p.SchoolSlug == subscription.SchoolSlug, ct);
        if (profile is { IsLocked: true, LockedReason: SubscriptionExpiredReason })
        {
            profile.IsLocked = false;
            profile.LockedReason = null;
            profile.LockedAt = null;
            profile.UpdatedAt = now;
        }
    }

    /// <summary>Cancel is a flag, never a revocation. Nothing is deleted or locked here.</summary>
    private static void ApplyCancel(Subscription subscription, DateTimeOffset now)
    {
        subscription.CancelAtPeriodEnd = true;
        if (subscription.Status != SubscriptionStatuses.Expired)
        {
            subscription.Status = SubscriptionStatuses.Canceling;
        }

        subscription.UpdatedAt = now;
    }

    /// <summary>Expiry downgrades and locks. It never deletes. The background expiration service does the same work without a provider event.</summary>
    private async Task ApplyEndedAsync(Subscription subscription, DateTimeOffset now, CancellationToken ct)
    {
        subscription.Status = SubscriptionStatuses.Expired;
        subscription.UpdatedAt = now;

        var profile = await db.PremiumProfiles.FirstOrDefaultAsync(p => p.SchoolSlug == subscription.SchoolSlug, ct);
        if (profile is { IsLocked: false })
        {
            profile.IsLocked = true;
            profile.LockedReason = SubscriptionExpiredReason;
            profile.LockedAt = now;
            profile.UpdatedAt = now;
        }
    }

    /// <summary>Matches by our subscription id, then by provider reference, then by school (one subscription row per school).</summary>
    private async Task<Subscription?> FindSubscriptionAsync(BillingEvent evt, CancellationToken ct)
    {
        if (evt.SubscriptionId is { } id)
        {
            var byId = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (evt.ProviderSubscriptionRef is { } providerRef)
        {
            var byRef = await db.Subscriptions.FirstOrDefaultAsync(s => s.ProviderRef == providerRef, ct);
            if (byRef is not null)
            {
                return byRef;
            }
        }

        if (evt.SchoolSlug is { } slug)
        {
            return await db.Subscriptions.FirstOrDefaultAsync(s => s.SchoolSlug == slug, ct);
        }

        return null;
    }

    private Task<bool> AlreadyRecordedAsync(string eventId, CancellationToken ct) =>
        db.WebhookEvents.AsNoTracking().AnyAsync(e => e.EventId == eventId, ct);
}
