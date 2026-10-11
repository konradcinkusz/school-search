using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Claims;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Models;
using SchoolSearch.Billing.RateLimiting;
using SchoolSearch.Billing.Security;

namespace SchoolSearch.Billing.Subscriptions;

/// <summary>
/// The sponsored block of a school. GET is anonymous and shows the public envelope. PUT is for the owner: it needs an
/// approved claim and a live subscription, and it saves the text only. Nothing here touches score, rank or order.
/// </summary>
public static class PremiumEndpoints
{
    public static IEndpointRouteBuilder MapPremiumEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/schools/{slug}/premium", GetPremium);

        app.MapPut("/api/v1/schools/{slug}/premium", PutPremium)
            .RequireAuthorization(AuthPolicies.VerifiedUser)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        return app;
    }

    internal static async Task<IResult> GetPremium(
        string slug,
        EntitlementService entitlement,
        BillingDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (InputRules.Slug(slug) is { } slugError)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", slugError);
        }

        var envelope = await BuildEnvelopeAsync(slug, entitlement, db, time.GetUtcNow(), ct);
        return Results.Json(envelope, ApiJson.Options);
    }

    internal static async Task<IResult> PutPremium(
        string slug,
        HttpRequest request,
        ClaimsPrincipal user,
        BillingDbContext db,
        SubjectHasher hasher,
        EntitlementService entitlement,
        TimeProvider time,
        CancellationToken ct)
    {
        if (InputRules.Slug(slug) is { } slugError)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", slugError);
        }

        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ClaimEndpoints.BillingUnavailable();
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<PremiumProfileRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var validation = PremiumValidator.Validate(body);
        if (validation.Draft is not { } draft)
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status400BadRequest,
                validation.ErrorCode ?? "validation_failed",
                validation.Message ?? "Request is invalid.");
        }

        var now = time.GetUtcNow();
        var owner = hasher.Hash(subject);

        var approved = await db.SchoolClaims.AsNoTracking().AnyAsync(
            c => c.SchoolSlug == slug && c.OwnerHash == owner && c.Status == ClaimStatuses.Approved,
            ct);
        if (!approved)
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status403Forbidden,
                "claim_not_approved",
                "A moderator must approve your claim before you can publish a profile.");
        }

        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolSlug == slug && s.OwnerHash == owner, ct);
        if (!IsLiveAt(subscription, now))
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status403Forbidden,
                "subscription_inactive",
                "An active subscription is needed to publish a profile.");
        }

        var profile = await db.PremiumProfiles.FirstOrDefaultAsync(p => p.SchoolSlug == slug, ct);
        if (profile is null)
        {
            profile = new PremiumProfile { SchoolSlug = slug };
            db.PremiumProfiles.Add(profile);
        }

        // Saving text does not lift a lock: a locked profile stays out of premium until a renewal unlocks it.
        profile.Headline = draft.Headline;
        profile.Description = draft.Description;
        profile.WebsiteUrl = draft.WebsiteUrl;
        profile.UpdatedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The primary key (one profile per school) caught a concurrent first save.
            return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "profile_conflict", "The profile was saved at the same time. Try again.");
        }

        var envelope = await BuildEnvelopeAsync(slug, entitlement, db, now, ct);
        return Results.Json(envelope, ApiJson.Options);
    }

    /// <summary>
    /// The public envelope. The profile is shown only while premium holds; tier, editorsVerified and enquiriesOpen
    /// are derived from the same rules as the entitlement service.
    /// </summary>
    internal static async Task<PremiumEnvelope> BuildEnvelopeAsync(
        string slug,
        EntitlementService entitlement,
        BillingDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var premium = await entitlement.IsPremiumAsync(slug, now, ct);
        var editorsVerified = await entitlement.HasApprovedClaimAsync(slug, ct);

        PremiumProfile? profile = null;
        if (premium)
        {
            profile = await db.PremiumProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.SchoolSlug == slug, ct);
        }

        var item = profile is null
            ? null
            : new PremiumProfileItem(profile.Headline, profile.Description, profile.WebsiteUrl);

        return new PremiumEnvelope(
            Sponsored: true,
            Tier: premium ? "premium" : "none",
            EditorsVerified: editorsVerified,
            Profile: item,
            Label: PremiumCopy.SponsoredLabel,
            EnquiriesOpen: profile is not null);
    }

    /// <summary>A subscription that may publish: active or canceling, with a period that has not ended.</summary>
    internal static bool IsLiveAt(Subscription? subscription, DateTimeOffset now)
    {
        if (subscription is null)
        {
            return false;
        }

        var live = subscription.Status == SubscriptionStatuses.Active
                   || subscription.Status == SubscriptionStatuses.Canceling;
        var periodEnd = subscription.CurrentPeriodEnd;

        return live && periodEnd is not null && periodEnd > now;
    }
}
