using Microsoft.EntityFrameworkCore;
using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.DevSeed;

/// <summary>
/// Writes the billing dev seed in one SaveChanges, so a failure stores nothing. Idempotent: when the marker row (the
/// seeded approved claim, with its fixed id) exists, nothing is written.
/// </summary>
public sealed class DevSeeder(BillingDbContext db, SubjectHasher hasher, TimeProvider clock)
{
    public async Task<DevSeedResponse> SeedAsync(string ownerSubject, string? moderatorSubject, CancellationToken ct)
    {
        if (await IsSeededAsync(ct))
        {
            return DevSeedResponse.Unchanged();
        }

        var rows = DevSeedBuilder.Build(
            hasher.Hash(ownerSubject),
            moderatorSubject is null ? null : hasher.Hash(moderatorSubject),
            clock.GetUtcNow());

        db.SchoolClaims.AddRange(rows.Claims);
        db.Subscriptions.AddRange(rows.Subscriptions);
        db.PremiumProfiles.AddRange(rows.Profiles);
        db.Leads.AddRange(rows.Leads);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent first call may have inserted the marker row. If so, this call changes nothing; otherwise rethrow.
            if (await IsSeededAsync(ct))
            {
                return DevSeedResponse.Unchanged();
            }

            throw;
        }

        return new DevSeedResponse(
            Claims: rows.Claims.Count,
            Subscriptions: rows.Subscriptions.Count,
            Profiles: rows.Profiles.Count,
            Enquiries: rows.Leads.Count,
            AlreadySeeded: false);
    }

    private Task<bool> IsSeededAsync(CancellationToken ct) =>
        db.SchoolClaims.AsNoTracking().AnyAsync(c => c.Id == DevSeedContent.Ids.ApprovedClaim, ct);
}
