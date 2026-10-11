using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>Maps a stored ModerationDecision to the statement of reasons shown to the notifier and the author.</summary>
public static class DsaStatements
{
    public static StatementOfReasons From(ModerationDecision decision) => new(
        decision.Id,
        decision.Action,
        decision.LegalOrTermsGround,
        decision.Facts,
        decision.Automated,
        decision.RedressInfo,
        decision.DecidedAt,
        decision.ReviewId,
        decision.NoticeId);
}
