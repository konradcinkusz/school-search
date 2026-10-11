using Microsoft.EntityFrameworkCore;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>Computed transparency counts (art. 24 style). Only aggregates; nothing per school, author or person.</summary>
internal static class DsaTransparency
{
    public static async Task<TransparencyReport> BuildAsync(ReviewsDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var noticesReceived = await db.Notices.CountAsync(ct);
        var noticesDecided = await db.Notices.CountAsync(n => n.Status == DsaStatus.Decided, ct);

        var byAction = await db.ModerationDecisions.AsNoTracking()
            .GroupBy(d => d.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var automated = await db.ModerationDecisions.CountAsync(d => d.Automated, ct);

        var complaintsReceived = await db.Complaints.CountAsync(ct);
        var upheld = await db.Complaints.CountAsync(c => c.Status == DsaStatus.Upheld, ct);
        var reversed = await db.Complaints.CountAsync(c => c.Status == DsaStatus.Reversed, ct);
        var pending = await db.Complaints.CountAsync(c => c.Status == DsaStatus.ComplaintReceived, ct);

        return new TransparencyReport(
            now,
            new TransparencyNotices(noticesReceived, noticesDecided),
            new TransparencyDecisions(
                byAction.Sum(x => x.Count),
                automated,
                byAction.ToDictionary(x => x.Action, x => x.Count)),
            new TransparencyComplaints(complaintsReceived, upheld, reversed, pending));
    }
}
