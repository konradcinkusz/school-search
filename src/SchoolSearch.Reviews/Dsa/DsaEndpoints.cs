using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>
/// DSA endpoints: notices (art. 16), statements of reasons (art. 17), complaints (art. 20), legal documents and the
/// transparency report (art. 11, 12 and 24). Moderation routes live in <see cref="DsaModerationEndpoints"/>.
/// Anonymous except complaints (account or notice token) and moderation (Moderator policy).
/// </summary>
public static class DsaEndpoints
{
    private const string ModeratorPolicy = "Moderator";

    public static IEndpointRouteBuilder MapDsaEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapPost("/notices", SubmitNotice).WithName("SubmitDsaNotice");
        api.MapGet("/notices/{id:guid}", GetNotice).WithName("GetDsaNoticeStatus");
        api.MapPost("/decisions/{id:guid}/complaints", SubmitComplaint).WithName("SubmitDsaComplaint");

        var legal = api.MapGroup("/legal");
        legal.MapGet("/terms", GetTerms).WithName("GetLegalTerms");
        legal.MapGet("/contact", GetContact).WithName("GetLegalContact");
        legal.MapGet("/transparency", GetTransparency).WithName("GetTransparencyReport");

        var moderation = api.MapGroup("/moderation").RequireAuthorization(ModeratorPolicy);
        moderation.MapGet("/queue", DsaModerationEndpoints.GetQueue).WithName("GetModerationQueue");
        moderation.MapPost("/reviews/{id:guid}/decision", DsaModerationEndpoints.DecideReview).WithName("DecideReview");
        moderation.MapPost("/replies/{reviewId:guid}/decision", DsaModerationEndpoints.DecideReply).WithName("DecideReply");
        moderation.MapPost("/notices/{id:guid}/decision", DsaModerationEndpoints.DecideNotice).WithName("DecideNotice");
        moderation.MapPost("/complaints/{id:guid}/decision", DsaModerationEndpoints.DecideComplaint).WithName("DecideComplaint");

        return app;
    }

    internal static async Task<IResult> SubmitNotice(
        HttpRequest request,
        ReviewsDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var (body, error) = await DsaHttp.ReadBodyAsync<DsaNoticeRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var validation = DsaValidation.ValidateNotice(body);
        if (validation.Value is not { } input)
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        if (input.ReviewId is { } reviewId && !await db.Reviews.AnyAsync(r => r.Id == reviewId, ct))
        {
            return DsaHttp.Error(StatusCodes.Status404NotFound, "not_found", "Review not found.");
        }

        var notice = new Notice
        {
            Id = Guid.NewGuid(),
            ReviewId = input.ReviewId,
            ContentReference = input.ContentReference,
            Explanation = input.Explanation,
            Category = input.Category,
            SubmitterName = input.SubmitterName,
            SubmitterEmail = input.SubmitterEmail,
            GoodFaithConfirmed = true,
            Status = DsaStatus.Received,
            AckToken = DsaTokens.NewToken(),
            ReceivedAt = clock.GetUtcNow(),
        };

        db.Notices.Add(notice);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created(
            $"/api/v1/notices/{notice.Id}",
            new NoticeReceipt(notice.Id, notice.AckToken, notice.ReceivedAt));
    }

    /// <summary>Status and statement of reasons of a notice. Unknown ids and wrong tokens get the same 404.</summary>
    internal static async Task<IResult> GetNotice(Guid id, string? token, ReviewsDbContext db, CancellationToken ct)
    {
        var notice = await db.Notices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notice is null || string.IsNullOrEmpty(token) || !DsaTokens.Matches(notice.AckToken, token))
        {
            return DsaHttp.Error(StatusCodes.Status404NotFound, "not_found", "Notice not found.");
        }

        var decision = await db.ModerationDecisions.AsNoTracking()
            .FirstOrDefaultAsync(d => d.NoticeId == id, ct);

        return TypedResults.Ok(new NoticeStatusView(
            notice.Id,
            notice.Status,
            notice.Category,
            notice.ContentReference,
            notice.ReviewId,
            notice.ReceivedAt,
            decision is null ? null : DsaStatements.From(decision)));
    }

    /// <summary>
    /// Complaint against a decision (art. 20). Allowed with the notice token of that decision, or by the author of the
    /// review the decision concerns. One complaint per decision.
    /// </summary>
    internal static async Task<IResult> SubmitComplaint(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        ReviewsDbContext db,
        DsaSubjectHasher hasher,
        TimeProvider clock,
        CancellationToken ct)
    {
        var (body, error) = await DsaHttp.ReadBodyAsync<DsaComplaintRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var validation = DsaValidation.ValidateComplaint(body);
        if (validation.Value is not { } input)
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        var decision = await db.ModerationDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (decision is null)
        {
            return DsaHttp.Error(StatusCodes.Status404NotFound, "not_found", "Decision not found.");
        }

        var refusal = await CheckComplainantAsync(decision, input.Token, user, db, hasher, ct);
        if (refusal is not null)
        {
            return refusal;
        }

        if (await db.Complaints.AnyAsync(c => c.DecisionId == id, ct))
        {
            return DsaHttp.Error(StatusCodes.Status409Conflict, "complaint_exists", "A complaint about this decision already exists.");
        }

        var complaint = new Complaint
        {
            Id = Guid.NewGuid(),
            DecisionId = id,
            Explanation = input.Explanation,
            AckToken = DsaTokens.NewToken(),
            Status = DsaStatus.ComplaintReceived,
            ReceivedAt = clock.GetUtcNow(),
        };

        db.Complaints.Add(complaint);
        await db.SaveChangesAsync(ct);

        return Results.Json(
            new ComplaintReceipt(complaint.Id, complaint.DecisionId, complaint.Status, complaint.ReceivedAt, complaint.AckToken),
            DsaJson.Options,
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Null when the caller may complain. A presented token is decisive: it must match the notice of this decision.
    /// Without a token the caller must be signed in and be the author of the review the decision concerns.
    /// </summary>
    private static async Task<IResult?> CheckComplainantAsync(
        ModerationDecision decision,
        string? token,
        ClaimsPrincipal user,
        ReviewsDbContext db,
        DsaSubjectHasher hasher,
        CancellationToken ct)
    {
        if (token is not null)
        {
            if (decision.NoticeId is { } noticeId)
            {
                var ackToken = await db.Notices.AsNoTracking()
                    .Where(n => n.Id == noticeId)
                    .Select(n => n.AckToken)
                    .FirstOrDefaultAsync(ct);

                if (ackToken is not null && DsaTokens.Matches(ackToken, token))
                {
                    return null;
                }
            }

            return DsaHttp.Error(StatusCodes.Status403Forbidden, "forbidden", "The notice token does not match this decision.");
        }

        var subject = user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subject))
        {
            return DsaHttp.Error(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in or provide the notice token.");
        }

        if (!hasher.IsConfigured)
        {
            return DsaHttp.Error(StatusCodes.Status503ServiceUnavailable, "pepper_not_configured", "Reviews subject pepper is not configured.");
        }

        if (decision.ReviewId is { } reviewId)
        {
            var authorHash = await db.Reviews.AsNoTracking()
                .Where(r => r.Id == reviewId)
                .Select(r => r.AuthorHash)
                .FirstOrDefaultAsync(ct);

            if (authorHash is not null && authorHash == hasher.Hash(subject))
            {
                return null;
            }
        }

        return DsaHttp.Error(StatusCodes.Status403Forbidden, "forbidden", "Only the review author can complain about this decision.");
    }

    internal static IResult GetTerms() =>
        TypedResults.Ok(new LegalTermsView(LegalDocuments.TermsVersion, LegalDocuments.TermsStatus, LegalDocuments.DraftNotice));

    /// <summary>The single point of contact. 503 contact_not_configured when no contact is set (the contact is never invented).</summary>
    internal static IResult GetContact(IOptions<DsaOptions> options)
    {
        var email = DsaValidation.Clean(options.Value.ContactEmail);
        var url = DsaValidation.Clean(options.Value.ContactUrl);
        if (email is null && url is null)
        {
            return DsaHttp.Error(StatusCodes.Status503ServiceUnavailable, "contact_not_configured", "Contact point is not configured.");
        }

        return TypedResults.Ok(new LegalContactView(email, url, LegalDocuments.ContactRole, ["pl"]));
    }

    internal static async Task<IResult> GetTransparency(ReviewsDbContext db, TimeProvider clock, CancellationToken ct) =>
        TypedResults.Ok(await DsaTransparency.BuildAsync(db, clock.GetUtcNow(), ct));
}
