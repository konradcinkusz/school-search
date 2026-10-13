namespace SchoolSearch.Billing.DevSeed;

/// <summary>Body of <c>POST /dev/seed</c>. Subjects are authservice subs of the seeded accounts.</summary>
public sealed record DevSeedRequest(DevSeedSubjects? Subjects);

/// <summary>Owner is required. Moderator is optional: without it the approval has no decider on record.</summary>
public sealed record DevSeedSubjects(string? Owner, string? Moderator);

/// <summary>
/// Response of <c>POST /dev/seed</c>. The counts are rows created by this call; a repeated call creates nothing, answers
/// alreadySeeded=true and reports zeros.
/// </summary>
public sealed record DevSeedResponse(int Claims, int Subscriptions, int Profiles, int Enquiries, bool AlreadySeeded)
{
    public static DevSeedResponse Unchanged() => new(0, 0, 0, 0, true);
}
