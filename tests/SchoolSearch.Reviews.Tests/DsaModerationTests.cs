using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Tests;

public sealed class DsaModerationTests : IClassFixture<ReviewsApiFactory>
{
    private const string Ground = "Art. 16 DSA oraz regulamin opinii, pkt 3.";
    private const string Facts = "Opinia zawiera imię nauczyciela. Sprawdzono treść ręcznie.";
    private const string NoticeExplanation = "Treść zawiera dane osobowe nauczyciela wymienione z nazwiska.";

    private readonly ReviewsApiFactory _factory;

    public DsaModerationTests(ReviewsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Notice_decision_creates_a_statement_of_reasons_the_notifier_can_read()
    {
        var review = DsaTestSupport.NewReview(_factory, "szkola-moderacja-1", "author-m1");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });

        var notifier = _factory.CreateClient();
        var receipt = await SubmitNoticeAsync(notifier, review.Id);
        var moderator = _factory.AsUser("mod-m1", true, "Admin");

        var decided = await moderator.PostAsync(
            $"/api/v1/moderation/notices/{receipt.Id}/decision",
            DsaTestSupport.JsonBody(new { action = "keep", legalOrTermsGround = Ground, facts = Facts }));

        Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        using var statement = await decided.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.False(statement!.RootElement.GetProperty("automated").GetBoolean());
        Assert.Equal("keep", statement.RootElement.GetProperty("action").GetString());
        Assert.False(string.IsNullOrWhiteSpace(statement.RootElement.GetProperty("redressInfo").GetString()));

        var status = await notifier.GetAsync($"/api/v1/notices/{receipt.Id}?token={receipt.AckToken}");
        using var body = await status.Content.ReadFromJsonAsync<JsonDocument>();
        var decision = body!.RootElement.GetProperty("decision");
        Assert.Equal(JsonValueKind.Object, decision.ValueKind);
        Assert.Equal(Facts, decision.GetProperty("facts").GetString());
        Assert.Equal(Ground, decision.GetProperty("legalOrTermsGround").GetString());
        Assert.False(decision.GetProperty("automated").GetBoolean());
        Assert.Equal(review.Id, decision.GetProperty("reviewId").GetGuid());
    }

    [Fact]
    public async Task Review_publish_sets_status_and_records_the_decision()
    {
        var review = DsaTestSupport.NewReview(_factory, "szkola-moderacja-2", "author-m2");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });
        var moderator = _factory.AsUser("mod-m2", true, "SuperAdmin");

        var response = await moderator.PostAsync(
            $"/api/v1/moderation/reviews/{review.Id}/decision",
            DsaTestSupport.JsonBody(new { action = "publish", legalOrTermsGround = Ground, facts = Facts }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await _factory.ReadDbAsync(db => db.Reviews.AsNoTracking().FirstAsync(r => r.Id == review.Id));
        Assert.Equal("published", stored.Status);
        Assert.NotNull(stored.PublishedAt);
        var decisions = await _factory.ReadDbAsync(db => db.ModerationDecisions.AsNoTracking().Where(d => d.ReviewId == review.Id).ToListAsync());
        var recorded = Assert.Single(decisions);
        Assert.False(recorded.Automated);
        Assert.Equal(HashOf("mod-m2"), recorded.DecidedBy);
    }

    [Fact]
    public async Task Ordinary_users_and_anonymous_callers_cannot_open_the_queue()
    {
        var anonymous = _factory.CreateClient();
        var user = _factory.AsUser("user-m3", true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/moderation/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/moderation/queue")).StatusCode);
    }

    [Fact]
    public async Task Decision_requires_a_ground_and_facts()
    {
        var review = DsaTestSupport.NewReview(_factory, "szkola-moderacja-4", "author-m4");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });
        var moderator = _factory.AsUser("mod-m4", true, "Admin");

        var response = await moderator.PostAsync(
            $"/api/v1/moderation/reviews/{review.Id}/decision",
            DsaTestSupport.JsonBody(new { action = "reject", legalOrTermsGround = "", facts = Facts }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transitions_that_the_rules_do_not_allow_are_refused()
    {
        var review = DsaTestSupport.NewReview(_factory, "szkola-moderacja-5", "author-m5");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });
        var moderator = _factory.AsUser("mod-m5", true, "Admin");

        var rejected = await moderator.PostAsync(
            $"/api/v1/moderation/reviews/{review.Id}/decision",
            DsaTestSupport.JsonBody(new { action = "reject", legalOrTermsGround = Ground, facts = Facts }));
        var removedAfterReject = await moderator.PostAsync(
            $"/api/v1/moderation/reviews/{review.Id}/decision",
            DsaTestSupport.JsonBody(new { action = "remove", legalOrTermsGround = Ground, facts = Facts }));

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, removedAfterReject.StatusCode);
    }

    [Fact]
    public async Task Notice_remove_removes_the_review_once_and_a_second_decision_is_refused()
    {
        var review = DsaTestSupport.NewReview(_factory, "szkola-moderacja-6", "author-m6", "published");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });
        var notice = await SubmitNoticeAsync(_factory.CreateClient(), review.Id);
        var moderator = _factory.AsUser("mod-m6", true, "Admin");
        var path = $"/api/v1/moderation/notices/{notice.Id}/decision";

        var removed = await moderator.PostAsync(path, DsaTestSupport.JsonBody(new { action = "remove", legalOrTermsGround = Ground, facts = Facts }));
        var again = await moderator.PostAsync(path, DsaTestSupport.JsonBody(new { action = "remove", legalOrTermsGround = Ground, facts = Facts }));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var stored = await _factory.ReadDbAsync(db => db.Reviews.AsNoTracking().FirstAsync(r => r.Id == review.Id));
        Assert.Equal("removed", stored.Status);
    }

    [Fact]
    public async Task Reply_can_be_published_only_under_a_published_review()
    {
        var review = DsaTestSupport.NewReview(_factory, "szkola-moderacja-7", "author-m7");
        review.Reply = new ReviewReply
        {
            ReviewId = review.Id,
            Body = "Odpowiedź dyrekcji: zapraszamy do kontaktu z sekretariatem szkoły.",
            RepresentativeLabel = "Dyrekcja szkoły",
            VerificationState = "unverified",
            AuthorHash = HashOf("school-user-m7"),
            Status = "pending",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });
        var moderator = _factory.AsUser("mod-m7", true, "Admin");
        var replyPath = $"/api/v1/moderation/replies/{review.Id}/decision";

        var early = await moderator.PostAsync(replyPath, DsaTestSupport.JsonBody(new { action = "publish", legalOrTermsGround = Ground, facts = Facts }));
        await moderator.PostAsync($"/api/v1/moderation/reviews/{review.Id}/decision", DsaTestSupport.JsonBody(new { action = "publish", legalOrTermsGround = Ground, facts = Facts }));
        var published = await moderator.PostAsync(replyPath, DsaTestSupport.JsonBody(new { action = "publish", legalOrTermsGround = Ground, facts = Facts }));

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var stored = await _factory.ReadDbAsync(db => db.Reviews.Include(r => r.Reply).AsNoTracking().FirstAsync(r => r.Id == review.Id));
        Assert.Equal("published", stored.Reply!.Status);
    }

    [Fact]
    public async Task Queue_puts_the_most_urgent_triage_label_first()
    {
        var calm = DsaTestSupport.NewReview(_factory, "szkola-moderacja-8", "author-m8a");
        var urgent = DsaTestSupport.NewReview(_factory, "szkola-moderacja-8", "author-m8b");
        urgent.CreatedAt = calm.CreatedAt.AddHours(1);
        await _factory.UseDbAsync(db =>
        {
            db.Reviews.AddRange(calm, urgent);
            db.ModerationTriages.AddRange(
                new ModerationTriage { ReviewId = calm.Id, Label = "ok", Note = "bez uwag", Model = "test", At = DateTimeOffset.UtcNow },
                new ModerationTriage { ReviewId = urgent.Id, Label = "likely_personal_data", Note = "nazwisko", Model = "test", At = DateTimeOffset.UtcNow });
            return db.SaveChangesAsync();
        });

        var response = await _factory.AsUser("mod-m8", true, "Admin").GetAsync("/api/v1/moderation/queue?status=pending&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        var ids = doc!.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
        Assert.True(ids.IndexOf(urgent.Id) >= 0 && ids.IndexOf(calm.Id) >= 0);
        Assert.True(ids.IndexOf(urgent.Id) < ids.IndexOf(calm.Id));
        var urgentItem = doc.RootElement.GetProperty("items").EnumerateArray().First(item => item.GetProperty("id").GetGuid() == urgent.Id);
        Assert.Equal("likely_personal_data", urgentItem.GetProperty("triage").GetProperty("label").GetString());
    }

    private string HashOf(string sub) => _factory.HashFor(sub);

    private static async Task<(Guid Id, string AckToken)> SubmitNoticeAsync(HttpClient client, Guid reviewId)
    {
        var response = await client.PostAsync("/api/v1/notices", DsaTestSupport.JsonBody(new
        {
            reviewId,
            explanation = NoticeExplanation,
            category = "personal_data",
            goodFaithConfirmed = true,
        }));
        response.EnsureSuccessStatusCode();
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return (doc!.RootElement.GetProperty("id").GetGuid(), doc.RootElement.GetProperty("ackToken").GetString()!);
    }
}
