using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Models;
using SchoolSearch.Billing.Security;

namespace SchoolSearch.Billing.Claims;

/// <summary>
/// Moderator endpoints for claims. Approval is the ONLY way a claim becomes approved, and an approved claim is the only
/// way a profile can be published. Moderator decisions record the deciding subject as a hash, never the raw sub.
/// </summary>
public static class ModerationEndpoints
{
    public const int MaxListed = 200;

    public static IEndpointRouteBuilder MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        var moderation = app.MapGroup("/api/v1/moderation");
        moderation.MapGet("/claims", ListClaims)
            .RequireAuthorization(AuthPolicies.Moderator);
        moderation.MapPost("/claims/{id:guid}/decision", DecideClaim)
            .RequireAuthorization(AuthPolicies.Moderator);

        return app;
    }

    internal static async Task<IResult> ListClaims(
        string? status,
        BillingDbContext db,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(status) && Array.IndexOf(ClaimStatuses.All, status) < 0)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Status must be pending, approved, rejected or revoked.");
        }

        var query = db.SchoolClaims.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(c => c.Status == status);
        }

        var rows = await query
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Take(MaxListed)
            .ToListAsync(ct);

        var items = rows.Select(c => new ModerationClaimItem(
            c.Id,
            c.SchoolSlug,
            c.RoleDescription,
            c.Status,
            c.CreatedAt,
            c.DecidedAt,
            c.Notes)).ToList();

        return Results.Json(new ModerationClaimsResponse(items), ApiJson.Options);
    }

    internal static async Task<IResult> DecideClaim(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        BillingDbContext db,
        SubjectHasher hasher,
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
            return ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "billing_unavailable", "Billing is temporarily unavailable.");
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<ModerationDecisionRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var action = body.Action?.Trim().ToLowerInvariant() ?? string.Empty;
        if (action is not (ClaimActions.Approve or ClaimActions.Reject or ClaimActions.Revoke))
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Action must be approve, reject or revoke.");
        }

        if (InputRules.Notes(body.Notes) is { } notesError)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", notesError);
        }

        var claim = await db.SchoolClaims.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "claim_not_found", "Claim not found.");
        }

        // Allowed transitions: pending -> approved | rejected; approved -> revoked. Anything else is refused.
        var target = (action, claim.Status) switch
        {
            (ClaimActions.Approve, ClaimStatuses.Pending) => ClaimStatuses.Approved,
            (ClaimActions.Reject, ClaimStatuses.Pending) => ClaimStatuses.Rejected,
            (ClaimActions.Revoke, ClaimStatuses.Approved) => ClaimStatuses.Revoked,
            _ => null,
        };

        if (target is null)
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status409Conflict,
                "invalid_transition",
                $"The claim is {claim.Status}; the action {action} is not allowed.");
        }

        var now = time.GetUtcNow();
        claim.Status = target;
        claim.DecidedAt = now;
        claim.DecidedBy = hasher.Hash(subject);
        claim.Notes = string.IsNullOrWhiteSpace(body.Notes) ? null : body.Notes.Trim();

        await db.SaveChangesAsync(ct);

        return Results.Json(new ClaimReceipt(claim.Id, claim.SchoolSlug, claim.Status), ApiJson.Options);
    }
}
