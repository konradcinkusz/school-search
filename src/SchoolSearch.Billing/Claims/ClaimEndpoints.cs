using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Models;
using SchoolSearch.Billing.RateLimiting;
using SchoolSearch.Billing.Security;

namespace SchoolSearch.Billing.Claims;

/// <summary>
/// Claim endpoints (owner side). A claim is a self-declaration; only a moderator's approval makes it count
/// ("Zweryfikowane przez redakcję"). One claim row per owner and school: a rejected claim is re-opened in place.
/// </summary>
public static class ClaimEndpoints
{
    public static IEndpointRouteBuilder MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/schools/{slug}/claims", CreateClaim)
            .RequireAuthorization(AuthPolicies.VerifiedUser)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        app.MapGet("/api/v1/claims/mine", ListMine)
            .RequireAuthorization(AuthPolicies.VerifiedUser);

        return app;
    }

    internal static async Task<IResult> CreateClaim(
        string slug,
        HttpRequest request,
        ClaimsPrincipal user,
        BillingDbContext db,
        SubjectHasher hasher,
        TimeProvider time,
        CancellationToken ct)
    {
        if (InputRules.Slug(slug) is { } slugError)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", slugError);
        }

        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return BillingUnavailable();
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<ClaimRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? Problem(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        if (InputRules.RoleDescription(body.RoleDescription) is { } roleError)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", roleError);
        }

        var role = body.RoleDescription!.Trim();
        var owner = hasher.Hash(subject);
        var now = time.GetUtcNow();
        var existing = await db.SchoolClaims.FirstOrDefaultAsync(
            c => c.SchoolSlug == slug && c.OwnerHash == owner, ct);

        if (existing is null)
        {
            var claim = new SchoolClaim
            {
                Id = Guid.NewGuid(),
                SchoolSlug = slug,
                OwnerHash = owner,
                RoleDescription = role,
                Status = ClaimStatuses.Pending,
                CreatedAt = now,
            };

            db.SchoolClaims.Add(claim);
            return await SaveAndReceipt(db, claim, ct);
        }

        switch (existing.Status)
        {
            case ClaimStatuses.Rejected:
                // Re-opened in place: the unique (school, owner) row is reused, the decision is cleared.
                existing.Status = ClaimStatuses.Pending;
                existing.RoleDescription = role;
                existing.CreatedAt = now;
                existing.DecidedAt = null;
                existing.DecidedBy = null;
                existing.Notes = null;
                return await SaveAndReceipt(db, existing, ct);

            case ClaimStatuses.Revoked:
                return Problem(StatusCodes.Status409Conflict, "claim_revoked", "A moderator has revoked the claim for this school.");

            default:
                return Problem(StatusCodes.Status409Conflict, "claim_exists", "You have already claimed this school.");
        }
    }

    internal static async Task<IResult> ListMine(
        ClaimsPrincipal user,
        SubjectHasher hasher,
        BillingDbContext db,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return BillingUnavailable();
        }

        var owner = hasher.Hash(subject);

        var claims = await db.SchoolClaims.AsNoTracking()
            .Where(c => c.OwnerHash == owner)
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(100)
            .ToListAsync(ct);

        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.OwnerHash == owner)
            .OrderBy(s => s.SchoolSlug)
            .Take(100)
            .ToListAsync(ct);

        var response = new MyClaimsResponse(
            claims.Select(c => new OwnClaimItem(c.Id, c.SchoolSlug, c.RoleDescription, c.Status, c.CreatedAt, c.DecidedAt)).ToList(),
            subscriptions.Select(ToSubscriptionItem).ToList());

        return Results.Json(response, ApiJson.Options);
    }

    internal static SubscriptionItem ToSubscriptionItem(Subscription subscription) => new(
        subscription.Id,
        subscription.SchoolSlug,
        subscription.PlanCode,
        subscription.Status,
        subscription.CurrentPeriodEnd,
        subscription.CancelAtPeriodEnd);

    private static async Task<IResult> SaveAndReceipt(BillingDbContext db, SchoolClaim claim, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique (school, owner) index caught a concurrent claim by the same owner.
            return Problem(StatusCodes.Status409Conflict, "claim_exists", "You have already claimed this school.");
        }

        return Results.Json(new ClaimReceipt(claim.Id, claim.SchoolSlug, claim.Status), ApiJson.Options, statusCode: StatusCodes.Status201Created);
    }

    private static IResult Problem(int status, string code, string message) =>
        ApiErrorWriter.Result(status, code, message);

    internal static IResult BillingUnavailable() =>
        ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "billing_unavailable", "Billing is temporarily unavailable.");
}
