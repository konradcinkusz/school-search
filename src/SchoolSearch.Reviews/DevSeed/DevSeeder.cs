using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.Dsa;
using SchoolSearch.Reviews.Settings;

namespace SchoolSearch.Reviews.DevSeed;

/// <summary>Authservice subjects the seed acts as. Each may be absent; the data that needs it is skipped.</summary>
public sealed record DevSeedSubjects(string? Parent, string? Parent2, string? Owner, string? Moderator);

/// <summary>Rows of the demo data present in the database, per table.</summary>
public sealed record DevSeedCounts(int Reviews, int Notices, int OwnerReplies, int Triage);

/// <summary>
/// The counts after the call, and whether the call found the demo data already complete and wrote nothing.
/// </summary>
public sealed record DevSeedOutcome(DevSeedCounts Counts, bool AlreadySeeded);

/// <summary>
/// Writes the local demo dataset for the Reviews service through the same entities and the same moderation service that
/// normal use relies on. Authors are hashed with the service pepper, as normal writes are. Moderation decisions go through
/// <see cref="DsaModerationService"/>, so each has Automated=false and a statement of reasons. Reached only through the
/// dev seed endpoint, which exists outside Production when enabled with a key.
/// </summary>
/// <remarks>
/// Idempotent and repairable. There is no "seeded" marker row: every step looks at its own fixed ids before it writes, so a
/// call that failed part-way is completed by the next call. On a relational provider the whole seed runs in one transaction,
/// so a failure rolls it back. InMemory has no transactions, so there the per-row checks carry the guarantee.
/// </remarks>
public sealed class DevSeeder(
    ReviewsDbContext db,
    SubjectHasher hasher,
    DsaModerationService moderation,
    IOptions<ReviewsOptions> reviewOptions,
    TimeProvider clock)
{
    /// <summary>School year of every demo review (2025/26).</summary>
    public const int DemoSchoolYearFrom = 2025;

    /// <summary>Recorded as the triage model. No model is called: the labels are set by this seed.</summary>
    public const string TriageModel = "dev-seed";

    public async Task<DevSeedOutcome> SeedAsync(DevSeedSubjects subjects, CancellationToken ct)
    {
        var parent = Clean(subjects.Parent);
        if (parent is null)
        {
            // Nothing is written without a reviewer. The answer reports whether the parent's demo review is already there.
            var present = await db.Reviews.AsNoTracking().AnyAsync(r => r.Id == DevSeedIds.ReviewParentSp1, ct);
            return new DevSeedOutcome(await CountAsync(ct), present);
        }

        bool written;
        if (db.Database.IsRelational())
        {
            // One transaction: a failure part-way rolls back every write, so the database is never left half seeded.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            written = await WriteMissingAsync(subjects, parent, ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            // InMemory has no transactions. The per-row checks make a rerun complete a seed that failed part-way.
            written = await WriteMissingAsync(subjects, parent, ct);
        }

        return new DevSeedOutcome(await CountAsync(ct), !written);
    }

    /// <summary>Counts the demo rows by their fixed ids. The same query runs after a write and on a repeat call.</summary>
    public async Task<DevSeedCounts> CountAsync(CancellationToken ct)
    {
        var reviews = await db.Reviews.AsNoTracking()
            .CountAsync(r => DevSeedIds.ReviewIds.Contains(r.Id), ct);
        var notices = await db.Notices.AsNoTracking()
            .CountAsync(n => DevSeedIds.NoticeIds.Contains(n.Id), ct);
        var replies = await db.ReviewReplies.AsNoTracking()
            .CountAsync(reply => DevSeedIds.ReviewIds.Contains(reply.ReviewId), ct);
        var triage = await db.ModerationTriages.AsNoTracking()
            .CountAsync(row => DevSeedIds.ReviewIds.Contains(row.ReviewId), ct);

        return new DevSeedCounts(reviews, notices, replies, triage);
    }

    /// <summary>
    /// Writes every demo row that is missing, in dependency order: content first, then the moderation decisions, the
    /// complaint, and the keep decision. Returns true when at least one row was written.
    /// </summary>
    private async Task<bool> WriteMissingAsync(DevSeedSubjects subjects, string parent, CancellationToken ct)
    {
        var parent2 = Clean(subjects.Parent2);
        var owner = Clean(subjects.Owner);
        var moderator = Clean(subjects.Moderator);
        var now = clock.GetUtcNow();
        var terms = reviewOptions.Value.TermsVersion;
        var parentHash = hasher.Hash(parent);
        var written = false;

        var reviews = new List<Review>
        {
            NewReview(DevSeedIds.ReviewParentSp1, "demo-sp-1", parentHash, 5, DevSeedContent.Title1, DevSeedContent.Body1, ReviewStatuses.Published, now, terms),
            NewReview(DevSeedIds.ReviewParentSp2, "demo-sp-2", parentHash, 3, DevSeedContent.Title2, DevSeedContent.Body2, ReviewStatuses.Published, now, terms),
            NewReview(DevSeedIds.ReviewParentSp3Pending, "demo-sp-3", parentHash, 4, DevSeedContent.Title5, DevSeedContent.Body5, ReviewStatuses.Pending, now, terms),
        };

        var replies = new List<ReviewReply>();
        if (owner is not null)
        {
            var ownerHash = hasher.Hash(owner);
            if (ownerHash != parentHash)
            {
                replies.Add(new ReviewReply
                {
                    ReviewId = DevSeedIds.ReviewParentSp1,
                    Body = DevSeedContent.OwnerReplyBody,
                    RepresentativeLabel = DevSeedContent.OwnerReplyLabel,
                    VerificationState = VerificationStates.Unverified,
                    AuthorHash = ownerHash,
                    Status = ReplyStatuses.Published,
                    CreatedAt = now,
                });
            }
        }

        var notices = new List<Notice>
        {
            NewNotice(
                DevSeedIds.NoticeOpen,
                DevSeedIds.ReviewParentSp2,
                ContentReferenceFor(DevSeedIds.ReviewParentSp2),
                DevSeedContent.NoticeOpenExplanation,
                NoticeCategories.Other,
                DevSeedIds.NoticeOpenToken,
                now),
        };

        if (parent2 is not null)
        {
            var parent2Hash = hasher.Hash(parent2);
            reviews.Add(NewReview(DevSeedIds.ReviewParent2Sp1, "demo-sp-1", parent2Hash, 4, DevSeedContent.Title3, DevSeedContent.Body3, ReviewStatuses.Published, now, terms));
            reviews.Add(NewReview(DevSeedIds.ReviewParent2Sp3, "demo-sp-3", parent2Hash, 2, DevSeedContent.Title4, DevSeedContent.Body4, ReviewStatuses.Published, now, terms));

            if (moderator is not null)
            {
                reviews.Add(NewReview(DevSeedIds.ReviewParent2Sp2Removed, "demo-sp-2", parent2Hash, 1, DevSeedContent.Title6, DevSeedContent.Body6, ReviewStatuses.Published, now, terms));
                notices.Add(NewNotice(
                    DevSeedIds.NoticeRemoved,
                    DevSeedIds.ReviewParent2Sp2Removed,
                    ContentReferenceFor(DevSeedIds.ReviewParent2Sp2Removed),
                    DevSeedContent.NoticeRemovedExplanation,
                    NoticeCategories.Defamation,
                    DevSeedIds.NoticeRemovedToken,
                    now));
            }
        }

        if (moderator is not null)
        {
            notices.Add(NewNotice(
                DevSeedIds.NoticeKept,
                null,
                DevSeedContent.ContentReferenceOtherNotice,
                DevSeedContent.NoticeKeptExplanation,
                NoticeCategories.Other,
                DevSeedIds.NoticeKeptToken,
                now));
        }

        // Advisory labels only (no model call). They order the moderator queue and are never shown to readers.
        var triage = new List<ModerationTriage>
        {
            NewTriage(DevSeedIds.ReviewParentSp3Pending, TriageLabels.NeedsAttention, now),
            NewTriage(DevSeedIds.ReviewParentSp2, TriageLabels.Unclear, now),
        };

        // Content rows: only the ones whose fixed id is not in the database yet.
        var existingReviewIds = await db.Reviews.AsNoTracking()
            .Where(r => DevSeedIds.ReviewIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct);
        var existingReplyIds = await db.ReviewReplies.AsNoTracking()
            .Where(reply => DevSeedIds.ReviewIds.Contains(reply.ReviewId))
            .Select(reply => reply.ReviewId)
            .ToListAsync(ct);
        var existingNoticeIds = await db.Notices.AsNoTracking()
            .Where(n => DevSeedIds.NoticeIds.Contains(n.Id))
            .Select(n => n.Id)
            .ToListAsync(ct);
        var existingTriageIds = await db.ModerationTriages.AsNoTracking()
            .Where(row => DevSeedIds.ReviewIds.Contains(row.ReviewId))
            .Select(row => row.ReviewId)
            .ToListAsync(ct);

        var newReviews = reviews.Where(r => !existingReviewIds.Contains(r.Id)).ToList();
        var newReplies = replies.Where(reply => !existingReplyIds.Contains(reply.ReviewId)).ToList();
        var newNotices = notices.Where(n => !existingNoticeIds.Contains(n.Id)).ToList();
        var newTriage = triage.Where(row => !existingTriageIds.Contains(row.ReviewId)).ToList();

        if (newReviews.Count + newReplies.Count + newNotices.Count + newTriage.Count > 0)
        {
            db.Reviews.AddRange(newReviews);
            db.ReviewReplies.AddRange(newReplies);
            db.ModerationTriages.AddRange(newTriage);
            db.Notices.AddRange(newNotices);
            await db.SaveChangesAsync(ct);
            written = true;
        }

        if (moderator is not null && parent2 is not null)
        {
            // Removal of the review through its notice: the statement of reasons is written by the moderation service.
            var removalStatus = await db.Notices.AsNoTracking()
                .Where(n => n.Id == DevSeedIds.NoticeRemoved)
                .Select(n => n.Status)
                .SingleAsync(ct);

            if (removalStatus == NoticeStatuses.Received)
            {
                var removal = await moderation.DecideNoticeAsync(
                    DevSeedIds.NoticeRemoved,
                    moderator,
                    new DecisionInput(ModerationActions.Remove, DevSeedContent.RemovalGround, DevSeedContent.RemovalFacts),
                    ct);

                if (removal.Value is null)
                {
                    throw new InvalidOperationException(FailureMessage("removal", removal.Failure));
                }

                written = true;
            }

            // The appeal of that decision: a complaint in the received state, with its own acknowledgement token.
            var complaintExists = await db.Complaints.AsNoTracking().AnyAsync(c => c.Id == DevSeedIds.Complaint, ct);
            if (!complaintExists)
            {
                var removalDecisionId = await db.ModerationDecisions.AsNoTracking()
                    .Where(d => d.NoticeId == DevSeedIds.NoticeRemoved)
                    .Select(d => d.Id)
                    .SingleAsync(ct);

                db.Complaints.Add(new Complaint
                {
                    Id = DevSeedIds.Complaint,
                    DecisionId = removalDecisionId,
                    Explanation = DevSeedContent.ComplaintExplanation,
                    AckToken = DevSeedIds.ComplaintToken,
                    Status = ComplaintStatuses.Received,
                    ReceivedAt = now,
                });
                await db.SaveChangesAsync(ct);
                written = true;
            }
        }

        if (moderator is not null)
        {
            var keptStatus = await db.Notices.AsNoTracking()
                .Where(n => n.Id == DevSeedIds.NoticeKept)
                .Select(n => n.Status)
                .SingleAsync(ct);

            if (keptStatus == NoticeStatuses.Received)
            {
                var kept = await moderation.DecideNoticeAsync(
                    DevSeedIds.NoticeKept,
                    moderator,
                    new DecisionInput(ModerationActions.Keep, DevSeedContent.KeptGround, DevSeedContent.KeptFacts),
                    ct);

                if (kept.Value is null)
                {
                    throw new InvalidOperationException(FailureMessage("keep", kept.Failure));
                }

                written = true;
            }
        }

        return written;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ContentReferenceFor(Guid reviewId) =>
        "/szkoly/demo/opinie/" + reviewId.ToString("D");

    private static string FailureMessage(string step, DsaFailure? failure) =>
        $"Dev seed: the {step} decision was refused ({failure?.Code ?? "unknown"}).";

    private static Review NewReview(
        Guid id,
        string slug,
        string authorHash,
        int rating,
        string title,
        string body,
        string status,
        DateTimeOffset now,
        string terms) => new()
    {
        Id = id,
        SchoolSlug = slug,
        AuthorHash = authorHash,
        Rating = rating,
        Title = title,
        Body = body,
        SchoolYearFrom = DemoSchoolYearFrom,
        Status = status,
        CreatedAt = now,
        UpdatedAt = now,
        PublishedAt = status == ReviewStatuses.Published ? now : null,
        TermsVersion = terms,
    };

    private static Notice NewNotice(
        Guid id,
        Guid? reviewId,
        string contentReference,
        string explanation,
        string category,
        string ackToken,
        DateTimeOffset now) => new()
    {
        Id = id,
        ReviewId = reviewId,
        ContentReference = contentReference,
        Explanation = explanation,
        Category = category,
        SubmitterName = DevSeedContent.SubmitterName,
        SubmitterEmail = null,
        GoodFaithConfirmed = true,
        Status = NoticeStatuses.Received,
        AckToken = ackToken,
        ReceivedAt = now,
    };

    private static ModerationTriage NewTriage(Guid reviewId, string label, DateTimeOffset now) => new()
    {
        ReviewId = reviewId,
        Label = label,
        Note = DevSeedContent.TriageNote,
        Model = TriageModel,
        At = now,
    };
}
