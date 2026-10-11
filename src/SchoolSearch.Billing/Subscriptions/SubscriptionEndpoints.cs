using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Claims;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Models;
using SchoolSearch.Billing.Plans;
using SchoolSearch.Billing.RateLimiting;
using SchoolSearch.Billing.Security;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.Subscriptions;

/// <summary>
/// Checkout, cancel and resume. Plans are chosen by code and priced on the server; the client never sends an amount.
/// Checkout only creates the pending row and returns a URL: the payment provider's webhook (Payments/, F2) is what grants
/// access. Cancel and resume only flip the cancel-at-period-end flag; nothing is ever deleted.
/// </summary>
public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var subscriptions = app.MapGroup("/api/v1/subscriptions");

        subscriptions.MapPost("/checkout", Checkout)
            .RequireAuthorization(AuthPolicies.VerifiedUser)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        // Stopping or resuming billing is the owner's right whatever their e-mail state: Authenticated, ownership checked.
        subscriptions.MapPost("/{id:guid}/cancel", Cancel)
            .RequireAuthorization(AuthPolicies.Authenticated)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        subscriptions.MapPost("/{id:guid}/resume", Resume)
            .RequireAuthorization(AuthPolicies.Authenticated)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        return app;
    }

    internal static async Task<IResult> Checkout(
        HttpRequest request,
        ClaimsPrincipal user,
        BillingDbContext db,
        SubjectHasher hasher,
        PlanCatalog plans,
        BillingMode mode,
        ICheckoutGateway gateway,
        IOptions<BillingOptions> options,
        TimeProvider time,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ClaimEndpoints.BillingUnavailable();
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<CheckoutRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        if (InputRules.Slug(body.SchoolSlug) is { } slugError)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", slugError);
        }

        var slug = body.SchoolSlug!.Trim();
        var plan = plans.Find(body.PlanCode?.Trim() ?? string.Empty);
        if (plan is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "unknown_plan", "The plan does not exist.");
        }

        // Outside mock mode without a provider, refuse before writing anything.
        if (!mode.IsMock && !gateway.IsConfigured)
        {
            return ClaimEndpoints.BillingUnavailable();
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
                "A moderator must approve your claim before you can buy a plan.");
        }

        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.SchoolSlug == slug, ct);
        if (subscription is not null)
        {
            if (subscription.OwnerHash != owner)
            {
                return ApiErrorWriter.Result(StatusCodes.Status403Forbidden, "forbidden", "You are not allowed to do this.");
            }

            if (IsPaidAt(subscription, now))
            {
                return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "subscription_active", "This school already has an active subscription.");
            }

            // Re-purchase updates the one row for the school. Until a payment is confirmed it grants nothing.
            subscription.PlanCode = plan.Code;
            subscription.Status = SubscriptionStatuses.Pending;
            subscription.CancelAtPeriodEnd = false;
            subscription.UpdatedAt = now;
        }
        else
        {
            subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                SchoolSlug = slug,
                OwnerHash = owner,
                PlanCode = plan.Code,
                Status = SubscriptionStatuses.Pending,
                CancelAtPeriodEnd = false,
                CreatedAt = now,
                UpdatedAt = now,
            };

            db.Subscriptions.Add(subscription);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique school index caught a concurrent checkout for the same school.
            return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "subscription_conflict", "A checkout for this school is already in progress. Try again.");
        }

        var paymentUrl = mode.IsMock
            ? MockPaymentUrl(options.Value.CheckoutBaseUrl, subscription.Id)
            : await gateway.CreateCheckoutUrlAsync(new CheckoutSessionRequest(subscription.Id, slug, plan.Code), ct);

        if (paymentUrl is null)
        {
            return ClaimEndpoints.BillingUnavailable();
        }

        return Results.Json(new CheckoutReceipt(subscription.Id, paymentUrl), ApiJson.Options);
    }

    internal static async Task<IResult> Cancel(
        Guid id,
        ClaimsPrincipal user,
        SubjectHasher hasher,
        BillingDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ClaimEndpoints.BillingUnavailable();
        }

        var owner = hasher.Hash(subject);
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.OwnerHash == owner, ct);
        if (subscription is null)
        {
            return SubscriptionNotFound();
        }

        if (subscription.Status == SubscriptionStatuses.Canceling)
        {
            return Results.Json(ClaimEndpoints.ToSubscriptionItem(subscription), ApiJson.Options);
        }

        if (subscription.Status != SubscriptionStatuses.Active)
        {
            return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "invalid_state", "Only an active subscription can be cancelled.");
        }

        subscription.Status = SubscriptionStatuses.Canceling;
        subscription.CancelAtPeriodEnd = true;
        subscription.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return Results.Json(ClaimEndpoints.ToSubscriptionItem(subscription), ApiJson.Options);
    }

    internal static async Task<IResult> Resume(
        Guid id,
        ClaimsPrincipal user,
        SubjectHasher hasher,
        BillingDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ClaimEndpoints.BillingUnavailable();
        }

        var owner = hasher.Hash(subject);
        var subscription = await db.Subscriptions.FirstOrDefaultAsync(s => s.Id == id && s.OwnerHash == owner, ct);
        if (subscription is null)
        {
            return SubscriptionNotFound();
        }

        if (subscription.Status == SubscriptionStatuses.Active && !subscription.CancelAtPeriodEnd)
        {
            return Results.Json(ClaimEndpoints.ToSubscriptionItem(subscription), ApiJson.Options);
        }

        if (subscription.Status != SubscriptionStatuses.Canceling)
        {
            return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "invalid_state", "Only a subscription set to cancel can be resumed.");
        }

        var now = time.GetUtcNow();
        var periodEnd = subscription.CurrentPeriodEnd;
        if (periodEnd is null || periodEnd <= now)
        {
            return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "period_ended", "The paid period has ended; buy the plan again.");
        }

        subscription.Status = SubscriptionStatuses.Active;
        subscription.CancelAtPeriodEnd = false;
        subscription.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return Results.Json(ClaimEndpoints.ToSubscriptionItem(subscription), ApiJson.Options);
    }

    /// <summary>True while the subscription is paid and its period has not ended.</summary>
    internal static bool IsPaidAt(Subscription subscription, DateTimeOffset now)
    {
        var paid = subscription.Status == SubscriptionStatuses.Active
                   || subscription.Status == SubscriptionStatuses.Canceling;
        var periodEnd = subscription.CurrentPeriodEnd;

        return paid && periodEnd is not null && periodEnd > now;
    }

    /// <summary>The synthetic checkout URL of mock mode. Going there is not a payment: the simulate endpoint is.</summary>
    internal static string MockPaymentUrl(string checkoutBaseUrl, Guid subscriptionId)
    {
        var prefix = string.IsNullOrWhiteSpace(checkoutBaseUrl) ? string.Empty : checkoutBaseUrl.Trim().TrimEnd('/');
        return prefix + "/mock-checkout/" + subscriptionId.ToString("D");
    }

    private static IResult SubscriptionNotFound() =>
        ApiErrorWriter.Result(StatusCodes.Status404NotFound, "subscription_not_found", "Subscription not found.");
}
