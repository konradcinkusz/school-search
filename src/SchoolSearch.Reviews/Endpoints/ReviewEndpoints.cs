using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Contracts.Public;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.Errors;
using SchoolSearch.Reviews.Models;
using SchoolSearch.Reviews.RateLimiting;
using SchoolSearch.Reviews.Security;
using SchoolSearch.Reviews.Settings;

namespace SchoolSearch.Reviews.Endpoints;

/// <summary>
/// Review endpoints of the Reviews service (E1a). Only published reviews and published replies are public.
/// Writes go through the subject hash, never the raw sub, and never store an e-mail address.
/// </summary>
public static class ReviewEndpoints
{
    public const int MaxPage = 10_000;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MineLimit = 100;

    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var schools = app.MapGroup("/api/v1/schools/{slug}/reviews");
        schools.MapGet(string.Empty, ListPublished);
        schools.MapPost(string.Empty, CreateReview)
            .RequireAuthorization(AuthPolicies.VerifiedUser)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        var reviews = app.MapGroup("/api/v1/reviews");
        reviews.MapGet("/mine", ListMine)
            .RequireAuthorization(AuthPolicies.Authenticated);
        reviews.MapDelete("/{id:guid}", DeleteOwn)
            .RequireAuthorization(AuthPolicies.Authenticated)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);
        reviews.MapPost("/{id:guid}/reply", CreateReply)
            .RequireAuthorization(AuthPolicies.VerifiedUser)
            .RequireRateLimiting(RateLimitPolicies.AuthWrites);

        return app;
    }

    internal static async Task<IResult> ListPublished(
        string slug,
        string? page,
        string? pageSize,
        ReviewsDbContext db,
        CancellationToken ct)
    {
        var pageNumber = ParseClamped(page, 1, MaxPage, 1);
        var size = ParseClamped(pageSize, 1, MaxPageSize, DefaultPageSize);

        var published = db.Reviews.AsNoTracking()
            .Where(r => r.SchoolSlug == slug && r.Status == ReviewStatuses.Published);

        var total = await published.CountAsync(ct);
        var rows = await published
            .Include(r => r.Reply)
            .OrderByDescending(r => r.PublishedAt)
            .ThenBy(r => r.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var items = rows.Select(ToPublicItem).ToList();
        var envelope = new ReviewsEnvelope(true, ReviewsPublicCopy.UserGeneratedNotice, items, total, pageNumber, size);
        return Results.Json(envelope, ApiJson.Options);
    }

    internal static async Task<IResult> CreateReview(
        string slug,
        HttpRequest request,
        ClaimsPrincipal user,
        ReviewsDbContext db,
        SubjectHasher hasher,
        IOptions<ReviewsOptions> options,
        TimeProvider time,
        CancellationToken ct)
    {
        if (ReviewValidator.ValidateSlug(slug) is { } slugError)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", slugError);
        }

        if (!hasher.IsConfigured)
        {
            return ReviewsUnavailable();
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<CreateReviewRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? Problem(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        if (!string.Equals(body.AcceptedTermsVersion, options.Value.TermsVersion, StringComparison.Ordinal))
        {
            return Problem(StatusCodes.Status400BadRequest, "terms_not_accepted", "Accept the current terms version to publish a review.");
        }

        var now = time.GetUtcNow();
        var (draft, error) = ReviewValidator.ValidateReview(body, now);
        if (draft is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", error ?? "Request is invalid.");
        }

        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        var authorHash = hasher.Hash(subject);
        var duplicate = await db.Reviews.AnyAsync(
            r => r.SchoolSlug == slug && r.AuthorHash == authorHash && r.SchoolYearFrom == draft.SchoolYearFrom,
            ct);
        if (duplicate)
        {
            return DuplicateReview();
        }

        var review = new Review
        {
            Id = Guid.NewGuid(),
            SchoolSlug = slug,
            AuthorHash = authorHash,
            Rating = draft.Rating,
            Title = draft.Title,
            Body = draft.Body,
            SchoolYearFrom = draft.SchoolYearFrom,
            Status = ReviewStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now,
            TermsVersion = options.Value.TermsVersion,
        };

        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique index (school, author, year) caught a concurrent duplicate.
            return DuplicateReview();
        }

        return Results.Json(new ReviewReceipt(review.Id, review.Status), ApiJson.Options, statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> ListMine(
        ClaimsPrincipal user,
        SubjectHasher hasher,
        ReviewsDbContext db,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ReviewsUnavailable();
        }

        var authorHash = hasher.Hash(subject);
        var reviews = await db.Reviews.AsNoTracking()
            .Include(r => r.Reply)
            .Where(r => r.AuthorHash == authorHash)
            .OrderByDescending(r => r.CreatedAt)
            .Take(MineLimit)
            .ToListAsync(ct);

        var reviewIds = reviews.Select(r => r.Id).ToList();
        var decisions = await db.ModerationDecisions.AsNoTracking()
            .Where(d => d.ReviewId != null && reviewIds.Contains(d.ReviewId.Value))
            .OrderBy(d => d.DecidedAt)
            .ToListAsync(ct);

        var statementsByReview = decisions
            .GroupBy(d => d.ReviewId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(ToStatement).ToList());

        var items = reviews.Select(review => new OwnReviewItem(
            review.Id,
            review.SchoolSlug,
            review.Rating,
            review.Title,
            review.Body,
            FormatSchoolYear(review.SchoolYearFrom),
            review.Status,
            review.CreatedAt,
            review.PublishedAt,
            review.Reply is { } reply
                ? new OwnReplyItem(reply.Body, reply.RepresentativeLabel, reply.VerificationState, reply.Status)
                : null,
            StatementsFor(statementsByReview, review.Id))).ToList();

        return Results.Json(new MyReviewsResponse(items), ApiJson.Options);
    }

    internal static async Task<IResult> DeleteOwn(
        Guid id,
        ClaimsPrincipal user,
        SubjectHasher hasher,
        ReviewsDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ReviewsUnavailable();
        }

        // A review that is not the caller's is reported exactly like a missing one.
        var authorHash = hasher.Hash(subject);
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.AuthorHash == authorHash, ct);
        if (review is null)
        {
            return ReviewNotFound();
        }

        review.Status = ReviewStatuses.Removed;
        review.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);

        return Results.Json(new ReviewReceipt(review.Id, review.Status), ApiJson.Options);
    }

    internal static async Task<IResult> CreateReply(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        ReviewsDbContext db,
        SubjectHasher hasher,
        TimeProvider time,
        CancellationToken ct)
    {
        var subject = AuthClaims.Subject(user);
        if (subject is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized", "Sign in is required.");
        }

        if (!hasher.IsConfigured)
        {
            return ReviewsUnavailable();
        }

        var (body, bodyError) = await JsonBodyReader.ReadAsync<ReviewReplyRequest>(request, ct);
        if (bodyError is not null || body is null)
        {
            return bodyError ?? Problem(StatusCodes.Status400BadRequest, "validation_failed", "Request body is required.");
        }

        var (draft, error) = ReviewValidator.ValidateReply(body);
        if (draft is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "validation_failed", error ?? "Request is invalid.");
        }

        var review = await db.Reviews.Include(r => r.Reply).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (review is null || review.Status != ReviewStatuses.Published)
        {
            return ReviewNotFound();
        }

        var authorHash = hasher.Hash(subject);
        if (review.AuthorHash == authorHash)
        {
            return Problem(StatusCodes.Status403Forbidden, "own_review", "The author of a review cannot reply to it.");
        }

        if (review.Reply is not null)
        {
            return Problem(StatusCodes.Status409Conflict, "reply_exists", "This review already has a reply.");
        }

        var reply = new ReviewReply
        {
            ReviewId = review.Id,
            Body = draft.Body,
            RepresentativeLabel = draft.RepresentativeLabel,
            VerificationState = VerificationStates.Unverified,
            AuthorHash = authorHash,
            Status = ReplyStatuses.Pending,
            CreatedAt = time.GetUtcNow(),
        };

        db.ReviewReplies.Add(reply);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The primary key (one reply per review) caught a concurrent reply.
            return Problem(StatusCodes.Status409Conflict, "reply_exists", "This review already has a reply.");
        }

        return Results.Json(new ReviewReceipt(review.Id, reply.Status), ApiJson.Options, statusCode: StatusCodes.Status201Created);
    }

    private static ReviewItem ToPublicItem(Review review) => new(
        review.Id,
        review.Rating,
        review.Title,
        review.Body,
        FormatSchoolYear(review.SchoolYearFrom),
        review.CreatedAt.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        ReviewsPublicCopy.AuthorLabel,
        review.Reply is { Status: ReplyStatuses.Published } reply
            ? new ReviewReplyItem(reply.Body, reply.RepresentativeLabel, reply.VerificationState)
            : null);

    private static StatementOfReasonsItem ToStatement(ModerationDecision decision) => new(
        decision.Id,
        decision.Action,
        decision.LegalOrTermsGround,
        decision.Facts,
        decision.Automated,
        decision.RedressInfo,
        decision.DecidedAt);

    private static IReadOnlyList<StatementOfReasonsItem> StatementsFor(
        IReadOnlyDictionary<Guid, List<StatementOfReasonsItem>> byReview,
        Guid reviewId) =>
        byReview.TryGetValue(reviewId, out var statements) ? statements : new List<StatementOfReasonsItem>();

    /// <summary>"2025/26" for SchoolYearFrom 2025.</summary>
    private static string FormatSchoolYear(int startYear) =>
        $"{startYear}/{(startYear + 1) % 100:D2}";

    private static int ParseClamped(string? raw, int min, int max, int fallback)
    {
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static IResult Problem(int status, string code, string message) =>
        ApiErrorWriter.Result(status, code, message);

    private static IResult ReviewsUnavailable() =>
        ApiErrorWriter.Result(StatusCodes.Status503ServiceUnavailable, "reviews_unavailable", "Reviews are temporarily unavailable.");

    private static IResult ReviewNotFound() =>
        ApiErrorWriter.Result(StatusCodes.Status404NotFound, "review_not_found", "Review not found.");

    private static IResult DuplicateReview() =>
        ApiErrorWriter.Result(StatusCodes.Status409Conflict, "duplicate_review", "You have already reviewed this school for that school year.");
}
