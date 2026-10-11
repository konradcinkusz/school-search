using Microsoft.EntityFrameworkCore;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>
/// The only code path that publishes, rejects or removes a review, and the only writer of ModerationDecision.
/// Every decision has Automated=false. AI triage labels only order the moderator queue; they never decide.
/// Callers must check that Reviews:SubjectPepper is configured before calling (the hashed moderator id is required).
/// </summary>
public sealed class DsaModerationService(ReviewsDbContext db, DsaSubjectHasher hasher, TimeProvider clock)
{
    /// <summary>Action names recorded for a complaint outcome. They do not change content by themselves.</summary>
    public const string ComplaintUpheldAction = "complaint_upheld";
    public const string ComplaintReversedAction = "complaint_reversed";

    private const string ComplaintGround = "Rozpatrzenie skargi wewnętrznej (art. 20 DSA)";

    /// <summary>Allowed review actions per current status. A reinstatement is an explicit publish, never a side effect.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ReviewTransitions = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [DsaStatus.Pending] = ["publish", "reject", "remove"],
        [DsaStatus.Published] = ["remove"],
        [DsaStatus.Rejected] = ["publish"],
        [DsaStatus.Removed] = ["publish"],
    };

    private static readonly string[] DeletableReviewStatuses = [DsaStatus.Pending, DsaStatus.Published];

    public async Task<DsaResult<StatementOfReasons>> DecideReviewAsync(
        Guid reviewId,
        string moderatorSubject,
        DecisionInput input,
        CancellationToken ct)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null)
        {
            return DsaResult<StatementOfReasons>.Fail(StatusCodes.Status404NotFound, "not_found", "Review not found.");
        }

        if (!ReviewTransitions.TryGetValue(review.Status, out var allowed) || !allowed.Contains(input.Action))
        {
            return DsaResult<StatementOfReasons>.Fail(
                StatusCodes.Status409Conflict,
                "invalid_transition",
                "The decision is not allowed for the review's current status.");
        }

        var now = clock.GetUtcNow();
        review.Status = ReviewStatusFor(input.Action);
        review.UpdatedAt = now;
        if (review.Status == DsaStatus.Published)
        {
            review.PublishedAt = now;
        }

        var decision = NewDecision(input, moderatorSubject, now, reviewId, noticeId: null);
        db.ModerationDecisions.Add(decision);
        await db.SaveChangesAsync(ct);

        return DsaResult<StatementOfReasons>.Ok(DsaStatements.From(decision));
    }

    /// <summary>Publishes or rejects a pending right-of-reply. A reply can be published only under a published review.</summary>
    public async Task<DsaResult<StatementOfReasons>> DecideReplyAsync(
        Guid reviewId,
        string moderatorSubject,
        DecisionInput input,
        CancellationToken ct)
    {
        var review = await db.Reviews.Include(r => r.Reply).FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null || review.Reply is not { } reply)
        {
            return DsaResult<StatementOfReasons>.Fail(StatusCodes.Status404NotFound, "not_found", "Reply not found.");
        }

        if (reply.Status != DsaStatus.Pending)
        {
            return DsaResult<StatementOfReasons>.Fail(StatusCodes.Status409Conflict, "invalid_transition", "The reply is not pending.");
        }

        var publish = input.Action == "publish";
        if (publish && review.Status != DsaStatus.Published)
        {
            return DsaResult<StatementOfReasons>.Fail(
                StatusCodes.Status409Conflict,
                "review_not_published",
                "A reply can be published only under a published review.");
        }

        reply.Status = publish ? DsaStatus.Published : DsaStatus.Rejected;

        var decision = NewDecision(input, moderatorSubject, clock.GetUtcNow(), reviewId, noticeId: null);
        db.ModerationDecisions.Add(decision);
        await db.SaveChangesAsync(ct);

        return DsaResult<StatementOfReasons>.Ok(DsaStatements.From(decision));
    }

    /// <summary>
    /// Records the outcome for a notice. "remove" removes the notified review when it is pending or published.
    /// Each notice gets one decision.
    /// </summary>
    public async Task<DsaResult<StatementOfReasons>> DecideNoticeAsync(
        Guid noticeId,
        string moderatorSubject,
        DecisionInput input,
        CancellationToken ct)
    {
        var notice = await db.Notices.FirstOrDefaultAsync(n => n.Id == noticeId, ct);
        if (notice is null)
        {
            return DsaResult<StatementOfReasons>.Fail(StatusCodes.Status404NotFound, "not_found", "Notice not found.");
        }

        if (notice.Status != DsaStatus.Received)
        {
            return DsaResult<StatementOfReasons>.Fail(StatusCodes.Status409Conflict, "already_decided", "The notice already has a decision.");
        }

        var now = clock.GetUtcNow();
        if (input.Action == "remove" && notice.ReviewId is { } reviewId)
        {
            var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
            if (review is null)
            {
                return DsaResult<StatementOfReasons>.Fail(
                    StatusCodes.Status409Conflict,
                    "review_not_found",
                    "The notified content no longer exists.");
            }

            if (DeletableReviewStatuses.Contains(review.Status))
            {
                review.Status = DsaStatus.Removed;
                review.UpdatedAt = now;
            }
        }

        notice.Status = DsaStatus.Decided;

        var decision = NewDecision(input, moderatorSubject, now, notice.ReviewId, notice.Id);
        db.ModerationDecisions.Add(decision);
        await db.SaveChangesAsync(ct);

        return DsaResult<StatementOfReasons>.Ok(DsaStatements.From(decision));
    }

    /// <summary>
    /// Records the outcome of an internal complaint against a decision. The outcome is a ModerationDecision with a
    /// complaint action. It never changes content on its own: a reinstatement is a separate review decision.
    /// </summary>
    public async Task<DsaResult<ComplaintOutcomeView>> DecideComplaintAsync(
        Guid complaintId,
        string moderatorSubject,
        ComplaintDecisionInput input,
        CancellationToken ct)
    {
        var complaint = await db.Complaints.FirstOrDefaultAsync(c => c.Id == complaintId, ct);
        if (complaint is null)
        {
            return DsaResult<ComplaintOutcomeView>.Fail(StatusCodes.Status404NotFound, "not_found", "Complaint not found.");
        }

        if (complaint.Status != DsaStatus.ComplaintReceived)
        {
            return DsaResult<ComplaintOutcomeView>.Fail(StatusCodes.Status409Conflict, "already_decided", "The complaint already has an outcome.");
        }

        var original = await db.ModerationDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == complaint.DecisionId, ct);
        if (original is null)
        {
            return DsaResult<ComplaintOutcomeView>.Fail(
                StatusCodes.Status409Conflict,
                "decision_not_found",
                "The complained-about decision no longer exists.");
        }

        var upheld = input.Outcome == "upheld";
        var now = clock.GetUtcNow();
        complaint.Status = upheld ? DsaStatus.Upheld : DsaStatus.Reversed;

        var outcomeAction = upheld ? ComplaintUpheldAction : ComplaintReversedAction;
        var decision = NewDecision(
            new DecisionInput(outcomeAction, ComplaintGround, input.Facts),
            moderatorSubject,
            now,
            original.ReviewId,
            noticeId: null);
        db.ModerationDecisions.Add(decision);
        await db.SaveChangesAsync(ct);

        return DsaResult<ComplaintOutcomeView>.Ok(new ComplaintOutcomeView(
            complaint.Id,
            complaint.DecisionId,
            complaint.Status,
            DsaStatements.From(decision)));
    }

    /// <summary>
    /// The moderator queue: open reviews, replies and notices, ordered by AI triage label (most urgent first),
    /// then by age. Labels are advisory. status: open (default), pending (reviews and replies) or received (notices).
    /// </summary>
    public async Task<ModerationQueuePage> QueueAsync(string filter, int page, int pageSize, CancellationToken ct)
    {
        var candidates = new List<QueueCandidate>();

        if (filter is "open" or "pending")
        {
            candidates.AddRange(await db.Reviews.AsNoTracking()
                .Where(r => r.Status == DsaStatus.Pending)
                .Select(r => new QueueCandidate("review", r.Id, r.CreatedAt, r.Id))
                .ToListAsync(ct));

            candidates.AddRange(await db.Reviews.AsNoTracking()
                .Where(r => r.Reply != null && r.Reply.Status == DsaStatus.Pending)
                .Select(r => new QueueCandidate("reply", r.Id, r.Reply!.CreatedAt, r.Id))
                .ToListAsync(ct));
        }

        if (filter is "open" or "received")
        {
            candidates.AddRange(await db.Notices.AsNoTracking()
                .Where(n => n.Status == DsaStatus.Received)
                .Select(n => new QueueCandidate("notice", n.Id, n.ReceivedAt, n.ReviewId))
                .ToListAsync(ct));
        }

        var linkedIds = candidates
            .Where(c => c.ReviewId is not null)
            .Select(c => c.ReviewId!.Value)
            .Distinct()
            .ToArray();

        var triage = new Dictionary<Guid, ModerationTriage>();
        if (linkedIds.Length > 0)
        {
            triage = await db.ModerationTriages.AsNoTracking()
                .Where(t => linkedIds.Contains(t.ReviewId))
                .ToDictionaryAsync(t => t.ReviewId, ct);
        }

        var ranked = candidates
            .Select(c => (Candidate: c, Triage: LinkedTriage(c.ReviewId, triage)))
            .OrderBy(r => LabelRank(r.Triage?.Label))
            .ThenBy(r => r.Candidate.CreatedAt)
            .ThenBy(r => r.Candidate.Id)
            .ToList();

        var pageRows = ranked.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var reviewIds = new HashSet<Guid>();
        var noticeIds = new List<Guid>();
        foreach (var row in pageRows)
        {
            if (row.Candidate.Kind == "notice")
            {
                noticeIds.Add(row.Candidate.Id);
                if (row.Candidate.ReviewId is { } linked)
                {
                    reviewIds.Add(linked);
                }
            }
            else
            {
                reviewIds.Add(row.Candidate.Id);
            }
        }

        var reviews = new Dictionary<Guid, Review>();
        if (reviewIds.Count > 0)
        {
            var ids = reviewIds.ToArray();
            reviews = await db.Reviews.AsNoTracking()
                .Include(r => r.Reply)
                .Where(r => ids.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);
        }

        var notices = new Dictionary<Guid, Notice>();
        if (noticeIds.Count > 0)
        {
            var ids = noticeIds.ToArray();
            notices = await db.Notices.AsNoTracking()
                .Where(n => ids.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id, ct);
        }

        var items = new List<ModerationQueueItem>();
        foreach (var (candidate, triageRow) in pageRows)
        {
            ModerationTriageView? view = triageRow is null
                ? null
                : new ModerationTriageView(triageRow.Label, triageRow.Note, triageRow.Model, triageRow.At);

            var item = candidate.Kind switch
            {
                "review" => ReviewItem(candidate, reviews, view),
                "reply" => ReplyItem(candidate, reviews, view),
                _ => NoticeItem(candidate, notices, reviews, view),
            };

            if (item is not null)
            {
                items.Add(item);
            }
        }

        return new ModerationQueuePage(items, page, pageSize, ranked.Count);
    }

    private ModerationDecision NewDecision(DecisionInput input, string moderatorSubject, DateTimeOffset now, Guid? reviewId, Guid? noticeId) => new()
    {
        Id = Guid.NewGuid(),
        ReviewId = reviewId,
        NoticeId = noticeId,
        Action = input.Action,
        LegalOrTermsGround = input.LegalOrTermsGround,
        Facts = input.Facts,
        Automated = false,
        RedressInfo = LegalDocuments.RedressInfo,
        DecidedBy = hasher.Hash(moderatorSubject),
        DecidedAt = now,
    };

    private static string ReviewStatusFor(string action) => action switch
    {
        "publish" => DsaStatus.Published,
        "reject" => DsaStatus.Rejected,
        _ => DsaStatus.Removed,
    };

    private static ModerationTriage? LinkedTriage(Guid? reviewId, IReadOnlyDictionary<Guid, ModerationTriage> triage) =>
        reviewId is { } id && triage.TryGetValue(id, out var row) ? row : null;

    private static int LabelRank(string? label) => label switch
    {
        "likely_personal_data" => 0,
        "likely_abusive" => 1,
        "needs_attention" => 2,
        "unclear" => 3,
        "ok" => 4,
        _ => 5,
    };

    private static ModerationQueueItem? ReviewItem(QueueCandidate candidate, IReadOnlyDictionary<Guid, Review> reviews, ModerationTriageView? triage)
    {
        if (!reviews.TryGetValue(candidate.Id, out var review))
        {
            return null;
        }

        return new ModerationQueueItem(
            "review",
            review.Id,
            review.Id,
            review.SchoolSlug,
            review.CreatedAt,
            review.Rating,
            review.Title,
            review.Body,
            null,
            null,
            null,
            null,
            null,
            null,
            triage);
    }

    private static ModerationQueueItem? ReplyItem(QueueCandidate candidate, IReadOnlyDictionary<Guid, Review> reviews, ModerationTriageView? triage)
    {
        if (!reviews.TryGetValue(candidate.Id, out var review) || review.Reply is not { } reply)
        {
            return null;
        }

        return new ModerationQueueItem(
            "reply",
            review.Id,
            review.Id,
            review.SchoolSlug,
            reply.CreatedAt,
            null,
            null,
            reply.Body,
            reply.RepresentativeLabel,
            review.Title,
            review.Body,
            null,
            null,
            null,
            triage);
    }

    private static ModerationQueueItem? NoticeItem(
        QueueCandidate candidate,
        IReadOnlyDictionary<Guid, Notice> notices,
        IReadOnlyDictionary<Guid, Review> reviews,
        ModerationTriageView? triage)
    {
        if (!notices.TryGetValue(candidate.Id, out var notice))
        {
            return null;
        }

        string? slug = null;
        if (notice.ReviewId is { } linked && reviews.TryGetValue(linked, out var linkedReview))
        {
            slug = linkedReview.SchoolSlug;
        }

        return new ModerationQueueItem(
            "notice",
            notice.Id,
            notice.ReviewId,
            slug,
            notice.ReceivedAt,
            null,
            null,
            null,
            null,
            null,
            null,
            notice.Category,
            notice.ContentReference,
            notice.Explanation,
            triage);
    }

    /// <summary>Internal queue row: an open item and the time it entered the queue.</summary>
    internal sealed record QueueCandidate(string Kind, Guid Id, DateTimeOffset CreatedAt, Guid? ReviewId);
}
