namespace SchoolSearch.Billing.Tests;

public sealed class LeadsPurgeTests : IClassFixture<LeadsApiFactory>
{
    private readonly LeadsApiFactory _factory;

    public LeadsPurgeTests(LeadsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Purge_deletes_only_enquiries_past_their_expiry()
    {
        var slug = "szkola-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var expiredId = await _factory.SeedLeadAsync(slug, now.AddDays(-91), now.AddDays(-1));
        var freshId = await _factory.SeedLeadAsync(slug, now.AddDays(-10), now.AddDays(80));

        var purged = await _factory.PurgeAsync(now);

        Assert.True(purged >= 1, "The expired enquiry should have been purged.");
        Assert.Null(await _factory.FindLeadAsync(expiredId));
        Assert.NotNull(await _factory.FindLeadAsync(freshId));
    }

    [Fact]
    public async Task Purge_is_a_no_op_when_nothing_has_expired()
    {
        var slug = "szkola-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var freshId = await _factory.SeedLeadAsync(slug, now, now.AddDays(90));

        await _factory.PurgeAsync(now);

        Assert.NotNull(await _factory.FindLeadAsync(freshId));
    }
}
