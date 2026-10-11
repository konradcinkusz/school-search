using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Leads;

/// <summary>
/// Parent enquiries. POST is anonymous and rate-limited (honeypot, versioned consent). GET and DELETE are for the
/// school's approved representative only. The consent endpoint serves the versioned DRAFT text.
/// Nothing here logs e-mail addresses or message bodies.
/// </summary>
public static class LeadEndpoints
{
    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/schools/{slug}/enquiries", SubmitEnquiry)
            .WithName("SubmitParentEnquiry")
            .RequireRateLimiting(LeadRules.EnquiryRateLimitPolicy);

        api.MapGet("/schools/{slug}/enquiries", ListEnquiries)
            .WithName("ListSchoolEnquiries")
            .RequireAuthorization(LeadRules.VerifiedUserPolicy);

        api.MapDelete("/enquiries/{id:guid}", DeleteEnquiry)
            .WithName("DeleteParentEnquiry")
            .RequireAuthorization(LeadRules.VerifiedUserPolicy)
            .RequireRateLimiting(LeadRules.OwnerWriteRateLimitPolicy);

        api.MapGet("/legal/enquiry-consent", GetConsent).WithName("GetEnquiryConsent");

        return app;
    }

    /// <summary>
    /// Anonymous enquiry. A honeypot hit answers 202 and stores nothing. A school without an open premium profile
    /// answers 404 (SPEC-F). Consent is checked against the current LeadConsent.Version.
    /// </summary>
    internal static async Task<IResult> SubmitEnquiry(
        string slug,
        HttpRequest request,
        BillingDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var (body, error) = await LeadHttp.ReadBodyAsync<LeadEnquiryRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        // Honeypot first: a bot that fills the hidden field gets the same answer as a human and nothing is stored.
        if (!string.IsNullOrWhiteSpace(body.Website))
        {
            return LeadHttp.Received();
        }

        var validated = LeadValidation.Validate(body);
        if (validated.Value is not { } enquiry)
        {
            return LeadHttp.Problem(StatusCodes.Status400BadRequest, "validation_failed", validated.Error ?? "Request is invalid.");
        }

        if (!string.Equals(body.AcceptedConsentVersion?.Trim(), LeadConsent.Version, StringComparison.Ordinal))
        {
            return LeadHttp.Problem(StatusCodes.Status400BadRequest, "consent_required", "The current enquiry consent must be accepted.");
        }

        var now = clock.GetUtcNow();
        if (slug.Length > LeadRules.MaxSlugLength || !await LeadAccess.IsPremiumAsync(db, slug, now, ct))
        {
            return LeadHttp.Problem(StatusCodes.Status404NotFound, "not_found", "Enquiries are not open for this school.");
        }

        db.Leads.Add(new Lead
        {
            Id = Guid.NewGuid(),
            SchoolSlug = slug,
            ParentEmail = enquiry.Email,
            Message = enquiry.Message,
            ConsentVersion = LeadConsent.Version,
            ConsentAt = now,
            CreatedAt = now,
            ExpiresAt = now.AddDays(LeadRules.RetentionDays),
            Status = LeadRules.StatusNew,
        });
        await db.SaveChangesAsync(ct);

        return LeadHttp.Received();
    }

    /// <summary>
    /// Enquiries of a school, newest first, for its approved representative. With markRead=true, unread enquiries are
    /// marked read after the response is built, so the response shows the status as it was before this call.
    /// </summary>
    internal static async Task<IResult> ListEnquiries(
        string slug,
        ClaimsPrincipal user,
        IConfiguration configuration,
        BillingDbContext db,
        TimeProvider clock,
        CancellationToken ct,
        bool markRead = false)
    {
        var (ownerHash, error) = ResolveOwnerHash(user, configuration);
        if (ownerHash is null)
        {
            return error!;
        }

        if (!await LeadAccess.IsOwnerAsync(db, slug, ownerHash, ct))
        {
            return LeadHttp.Problem(StatusCodes.Status403Forbidden, "forbidden", "Only the school's approved representative can read its enquiries.");
        }

        var now = clock.GetUtcNow();
        var rows = await db.Leads
            .Where(l => l.SchoolSlug == slug && l.ExpiresAt > now)
            .OrderByDescending(l => l.CreatedAt)
            .Take(LeadRules.MaxListed)
            .ToListAsync(ct);

        var views = rows.Select(ToView).ToList();

        if (markRead)
        {
            var unread = rows.Where(l => l.Status == LeadRules.StatusNew).ToList();
            if (unread.Count > 0)
            {
                foreach (var lead in unread)
                {
                    lead.Status = LeadRules.StatusRead;
                }

                await db.SaveChangesAsync(ct);
            }
        }

        return TypedResults.Ok(new LeadListView(views));
    }

    /// <summary>
    /// Deletes one enquiry. An enquiry of another school answers 404, the same as an unknown id, so ids cannot be probed.
    /// </summary>
    internal static async Task<IResult> DeleteEnquiry(
        Guid id,
        ClaimsPrincipal user,
        IConfiguration configuration,
        BillingDbContext db,
        CancellationToken ct)
    {
        var (ownerHash, error) = ResolveOwnerHash(user, configuration);
        if (ownerHash is null)
        {
            return error!;
        }

        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null || !await LeadAccess.IsOwnerAsync(db, lead.SchoolSlug, ownerHash, ct))
        {
            return LeadHttp.Problem(StatusCodes.Status404NotFound, "not_found", "Enquiry not found.");
        }

        db.Leads.Remove(lead);
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    /// <summary>The consent text a parent must accept. Status "draft" until the wording is reviewed by a lawyer.</summary>
    internal static IResult GetConsent() =>
        TypedResults.Ok(new ConsentView(LeadConsent.Version, LeadConsent.Text, LeadConsent.Status));

    private static (string? OwnerHash, IResult? Error) ResolveOwnerHash(ClaimsPrincipal user, IConfiguration configuration)
    {
        var subject = LeadAccess.Subject(user);
        if (subject is null)
        {
            return (null, LeadHttp.Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in to manage enquiries."));
        }

        var ownerHash = LeadAccess.OwnerHash(configuration, subject);
        if (ownerHash is null)
        {
            return (null, LeadHttp.Problem(StatusCodes.Status503ServiceUnavailable, "pepper_not_configured", "Billing subject pepper is not configured."));
        }

        return (ownerHash, null);
    }

    private static LeadView ToView(Lead lead) => new(
        lead.Id,
        lead.ParentEmail,
        lead.Message,
        lead.ConsentVersion,
        lead.ConsentAt,
        lead.CreatedAt,
        lead.ExpiresAt,
        lead.Status);
}
