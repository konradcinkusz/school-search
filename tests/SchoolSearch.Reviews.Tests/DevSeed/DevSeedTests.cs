using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.DevSeed;

namespace SchoolSearch.Reviews.Tests;

/// <summary>
/// The local demo seed (POST /dev/seed). Each test builds its own host, so the database and the flags are isolated.
/// Subjects are test strings; only their keyed hashes are ever stored.
/// </summary>
public sealed class DevSeedTests
{
    private const string ParentSub = "sub-parent-demo-0001";
    private const string Parent2Sub = "sub-parent2-demo-0002";
    private const string OwnerSub = "sub-owner-demo-0003";
    private const string ModeratorSub = "sub-moderator-demo-0004";

    private const string SeedPath = "/dev/seed";

    /// <summary>ASCII stems of the words that would make a demo review read as a score. Polish text is JSON-escaped in responses, so only ASCII is matched.</summary>
    private static readonly string[] ScoringWords = { "wynik", "ranking", "score", "ocen", "punkt", "algorytm" };

    private static readonly Regex EmailPattern = new(
        @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly Regex PeselPattern = new(@"\b\d{11}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex PhonePattern = new(
        @"(\+48[ -]?)?\b\d{3}[ -]\d{3}[ -]\d{3}\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(2));

    private static readonly TableCounts EmptyCounts = new(0, 0, 0, 0, 0, 0);

    private static object AllSubjects() => new
    {
        subjects = new
        {
            parent = ParentSub,
            parent2 = Parent2Sub,
            owner = OwnerSub,
            moderator = ModeratorSub,
        },
    };

    private static HttpRequestMessage SeedRequest(string? key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SeedPath) { Content = JsonTestHelpers.JsonBody(body) };
        if (key is not null)
        {
            request.Headers.Add(DevSeedEndpoints.KeyHeader, key);
        }

        return request;
    }

    [Fact]
    public async Task Route_is_not_found_when_the_seed_is_disabled_by_default()
    {
        using var factory = new DevSeedFactory(false, DevSeedFactory.ConfiguredKey, "Testing");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(EmptyCounts, await factory.CountAllAsync());
    }

    [Fact]
    public async Task Route_is_not_found_when_enabled_without_a_key()
    {
        using var factory = new DevSeedFactory(true, string.Empty, "Testing");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(SeedRequest(string.Empty, AllSubjects()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Route_is_not_found_in_production_even_when_enabled_with_a_key()
    {
        using var factory = new DevSeedFactory(true, DevSeedFactory.ConfiguredKey, "Production");
        using var client = factory.CreateClient();

        var response = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(EmptyCounts, await factory.CountAllAsync());
    }

    [Fact]
    public async Task Missing_key_is_unauthorized_and_writes_nothing()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(SeedRequest(null, AllSubjects()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await JsonTestHelpers.ErrorCodeAsync(response));
        Assert.Equal(EmptyCounts, await factory.CountAllAsync());
    }

    [Fact]
    public async Task Wrong_key_is_unauthorized_and_writes_nothing()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();
        var sameLengthWrong = DevSeedFactory.ConfiguredKey[..^1] + "X";

        var sameLength = await client.SendAsync(SeedRequest(sameLengthWrong, AllSubjects()));
        var shorter = await client.SendAsync(SeedRequest("short", AllSubjects()));

        Assert.Equal(HttpStatusCode.Unauthorized, sameLength.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, shorter.StatusCode);
        Assert.Equal(EmptyCounts, await factory.CountAllAsync());
    }

    [Fact]
    public async Task Seed_with_the_key_creates_the_demo_data_and_a_repeat_creates_nothing()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();

        var first = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await JsonTestHelpers.ReadJsonAsync(first);

        Assert.Equal(6, firstBody.GetProperty("reviews").GetInt32());
        Assert.Equal(3, firstBody.GetProperty("notices").GetInt32());
        Assert.Equal(1, firstBody.GetProperty("ownerReplies").GetInt32());
        Assert.Equal(2, firstBody.GetProperty("triage").GetInt32());
        Assert.False(firstBody.GetProperty("alreadySeeded").GetBoolean());

        var before = await factory.CountAllAsync();
        Assert.Equal(new TableCounts(6, 1, 3, 2, 1, 2), before);

        var second = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await JsonTestHelpers.ReadJsonAsync(second);

        Assert.True(secondBody.GetProperty("alreadySeeded").GetBoolean());
        Assert.Equal(firstBody.GetProperty("reviews").GetInt32(), secondBody.GetProperty("reviews").GetInt32());
        Assert.Equal(firstBody.GetProperty("notices").GetInt32(), secondBody.GetProperty("notices").GetInt32());
        Assert.Equal(firstBody.GetProperty("ownerReplies").GetInt32(), secondBody.GetProperty("ownerReplies").GetInt32());
        Assert.Equal(firstBody.GetProperty("triage").GetInt32(), secondBody.GetProperty("triage").GetInt32());

        Assert.Equal(before, await factory.CountAllAsync());
    }

    [Fact]
    public async Task Without_a_moderator_the_moderated_rows_are_skipped_and_the_marker_still_holds()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();
        var subjects = new { subjects = new { parent = ParentSub } };

        var response = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, subjects));
        var body = await JsonTestHelpers.ReadJsonAsync(response);

        Assert.Equal(3, body.GetProperty("reviews").GetInt32());
        Assert.Equal(1, body.GetProperty("notices").GetInt32());
        Assert.Equal(0, body.GetProperty("ownerReplies").GetInt32());
        Assert.Equal(2, body.GetProperty("triage").GetInt32());
        Assert.False(body.GetProperty("alreadySeeded").GetBoolean());
        Assert.Equal(new TableCounts(3, 0, 1, 0, 0, 2), await factory.CountAllAsync());

        // The marker is in place, so a later call with every subject writes nothing new.
        var later = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));
        var laterBody = await JsonTestHelpers.ReadJsonAsync(later);
        Assert.True(laterBody.GetProperty("alreadySeeded").GetBoolean());
        Assert.Equal(3, laterBody.GetProperty("reviews").GetInt32());
        Assert.Equal(new TableCounts(3, 0, 1, 0, 0, 2), await factory.CountAllAsync());
    }

    [Fact]
    public async Task Without_any_subject_nothing_is_written()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, new { subjects = new { } }));
        var body = await JsonTestHelpers.ReadJsonAsync(response);

        Assert.Equal(0, body.GetProperty("reviews").GetInt32());
        Assert.False(body.GetProperty("alreadySeeded").GetBoolean());
        Assert.Equal(EmptyCounts, await factory.CountAllAsync());
    }

    [Fact]
    public async Task Seeded_rows_follow_the_domain_invariants_and_hash_the_subjects()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();
        await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));

        var reviews = await factory.WithDbAsync(db => db.Reviews.AsNoTracking().Include(r => r.Reply).ToListAsync());
        var notices = await factory.WithDbAsync(db => db.Notices.AsNoTracking().ToListAsync());
        var decisions = await factory.WithDbAsync(db => db.ModerationDecisions.AsNoTracking().ToListAsync());
        var complaints = await factory.WithDbAsync(db => db.Complaints.AsNoTracking().ToListAsync());
        var triage = await factory.WithDbAsync(db => db.ModerationTriages.AsNoTracking().ToListAsync());

        var allowedStatuses = new HashSet<string>(StringComparer.Ordinal) { "pending", "published", "removed" };
        foreach (var review in reviews)
        {
            Assert.True(review.Rating is >= 1 and <= 5, "rating out of range");
            Assert.True(review.Title.Length is >= 1 and <= 120, "title length");
            Assert.True(review.Body.Length is >= 40 and <= 4000, "body length");
            Assert.StartsWith("[DEMO] ", review.Title, StringComparison.Ordinal);
            Assert.Contains("demonstracyjne", review.Body, StringComparison.Ordinal);
            Assert.Contains(review.SchoolSlug, new[] { "demo-sp-1", "demo-sp-2", "demo-sp-3" });
            Assert.Equal(2025, review.SchoolYearFrom);
            Assert.Equal(ReviewsApiFactory.TermsVersion, review.TermsVersion);
            Assert.Equal(64, review.AuthorHash.Length);
            Assert.Contains(review.Status, allowedStatuses);
        }

        Assert.Contains(reviews, r => r.Status == "pending");
        Assert.Contains(reviews, r => r.Status == "removed");
        Assert.Contains(reviews, r => r.Status == "published" && r.PublishedAt is not null);

        // Authors are stored only as keyed hashes, exactly as a normal write stores them.
        var authorHashes = reviews.Select(r => r.AuthorHash).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(factory.HashFor(ParentSub), authorHashes);
        Assert.Contains(factory.HashFor(Parent2Sub), authorHashes);
        Assert.DoesNotContain(ParentSub, authorHashes);
        Assert.DoesNotContain(Parent2Sub, authorHashes);

        // The moderated review carries a statement of reasons written by the moderation path.
        var removed = Assert.Single(reviews, r => r.Status == "removed");
        var removalDecision = Assert.Single(decisions, d => d.ReviewId == removed.Id);
        Assert.Equal("remove", removalDecision.Action);
        Assert.False(removalDecision.Automated);
        Assert.Equal(factory.HashFor(ModeratorSub), removalDecision.DecidedBy);

        foreach (var decision in decisions)
        {
            Assert.False(decision.Automated);
            Assert.False(string.IsNullOrWhiteSpace(decision.RedressInfo));
            Assert.DoesNotContain(ModeratorSub, decision.DecidedBy, StringComparison.Ordinal);
        }

        foreach (var notice in notices)
        {
            Assert.True(notice.GoodFaithConfirmed);
            Assert.True(notice.Explanation.Length is >= 20 and <= 3000, "notice explanation length");
            Assert.Null(notice.SubmitterEmail);
            Assert.False(string.IsNullOrEmpty(notice.AckToken));
        }

        // Two decided notices (removal and keep), one open notice.
        Assert.Equal(2, notices.Count(n => n.Status == "decided"));
        Assert.Single(notices, n => n.Status == "received");
        var removedNotice = Assert.Single(notices, n => n.Status == "decided" && n.ReviewId == removed.Id);
        Assert.Equal(removalDecision.Id, Assert.Single(decisions, d => d.NoticeId == removedNotice.Id).Id);

        // The appeal of that decision: one complaint still in the received state.
        var complaint = Assert.Single(complaints);
        Assert.Equal("received", complaint.Status);
        Assert.Equal(removalDecision.Id, complaint.DecisionId);

        // The owner's right of reply: published, under a published review.
        var reply = Assert.Single(reviews.Where(r => r.Reply is not null).Select(r => r.Reply!));
        Assert.Equal("published", reply.Status);
        Assert.Equal(factory.HashFor(OwnerSub), reply.AuthorHash);
        Assert.Equal("unverified", reply.VerificationState);

        Assert.Equal(2, triage.Count());
        foreach (var row in triage)
        {
            Assert.Equal("dev-seed", row.Model);
            Assert.Contains(row.Label, new[] { "ok", "needs_attention", "likely_personal_data", "likely_abusive", "unclear" });
        }
    }

    [Fact]
    public async Task Seeded_text_has_no_personal_data_patterns_or_scoring_words()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();
        await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));

        var texts = await factory.WithDbAsync(async db =>
        {
            var collected = new List<string>();
            collected.AddRange(await db.Reviews.AsNoTracking().Select(r => r.Title).ToListAsync());
            collected.AddRange(await db.Reviews.AsNoTracking().Select(r => r.Body).ToListAsync());
            collected.AddRange(await db.ReviewReplies.AsNoTracking().Select(r => r.Body).ToListAsync());
            collected.AddRange(await db.ReviewReplies.AsNoTracking().Select(r => r.RepresentativeLabel).ToListAsync());
            collected.AddRange(await db.Notices.AsNoTracking().Select(n => n.Explanation).ToListAsync());
            collected.AddRange(await db.Notices.AsNoTracking().Select(n => n.SubmitterName ?? string.Empty).ToListAsync());
            collected.AddRange(await db.ModerationDecisions.AsNoTracking().Select(d => d.LegalOrTermsGround).ToListAsync());
            collected.AddRange(await db.ModerationDecisions.AsNoTracking().Select(d => d.Facts).ToListAsync());
            collected.AddRange(await db.Complaints.AsNoTracking().Select(c => c.Explanation).ToListAsync());
            collected.AddRange(await db.ModerationTriages.AsNoTracking().Select(t => t.Note).ToListAsync());
            return collected;
        });

        Assert.NotEmpty(texts);
        foreach (var text in texts)
        {
            Assert.DoesNotMatch(EmailPattern, text);
            Assert.DoesNotMatch(PeselPattern, text);
            Assert.DoesNotMatch(PhonePattern, text);
            Assert.DoesNotContain(ParentSub, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Parent2Sub, text, StringComparison.Ordinal);
            Assert.DoesNotContain(OwnerSub, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ModeratorSub, text, StringComparison.Ordinal);

            var lower = text.ToLowerInvariant();
            foreach (var word in ScoringWords)
            {
                Assert.DoesNotContain(word, lower, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task Seeded_notices_are_readable_with_their_acknowledgement_tokens()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();
        await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));

        var open = await client.GetAsync($"/api/v1/notices/{DevSeedIds.NoticeOpen:D}?token={DevSeedIds.NoticeOpenToken}");
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var openBody = await JsonTestHelpers.ReadJsonAsync(open);
        Assert.Equal("received", openBody.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, openBody.GetProperty("decision").ValueKind);

        var decided = await client.GetAsync($"/api/v1/notices/{DevSeedIds.NoticeRemoved:D}?token={DevSeedIds.NoticeRemovedToken}");
        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        var decidedBody = await JsonTestHelpers.ReadJsonAsync(decided);
        Assert.Equal("decided", decidedBody.GetProperty("status").GetString());
        Assert.Equal("remove", decidedBody.GetProperty("decision").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Public_reviews_show_only_the_published_seeded_reviews()
    {
        using var factory = new DevSeedFactory();
        using var client = factory.CreateClient();
        await client.SendAsync(SeedRequest(DevSeedFactory.ConfiguredKey, AllSubjects()));

        // demo-sp-1: two published reviews, one of them with the owner's reply.
        var firstSchool = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools/demo-sp-1/reviews"));
        Assert.Equal(2, firstSchool.GetProperty("total").GetInt32());
        Assert.Equal(2, firstSchool.GetProperty("items").GetArrayLength());

        // demo-sp-2: one published review. The removed review of that school stays hidden.
        var secondSchool = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools/demo-sp-2/reviews"));
        Assert.Equal(1, secondSchool.GetProperty("total").GetInt32());

        // demo-sp-3: one published review. The pending review of that school stays hidden.
        var thirdSchool = await JsonTestHelpers.ReadJsonAsync(await client.GetAsync("/api/v1/schools/demo-sp-3/reviews"));
        Assert.Equal(1, thirdSchool.GetProperty("total").GetInt32());
    }
}
