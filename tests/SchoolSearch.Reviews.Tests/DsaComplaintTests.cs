using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolSearch.Reviews.Tests;

public sealed class DsaComplaintTests : IClassFixture<DsaTestFactory>
{
    private const string Ground = "Art. 17 DSA oraz regulamin opinii, pkt 4.";
    private const string Facts = "Przejrzano opinię i historię zgłoszeń ponownie.";
    private const string Explanation = "Opinia opisuje faktycznie zrealizowaną wizytę w szkole.";

    private readonly DsaTestFactory _factory;

    public DsaComplaintTests(DsaTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Author_can_complain_once_about_a_decision_on_their_own_review()
    {
        var decisionId = await RejectReviewAsync("szkola-skarga-1", "author-c1");
        var author = _factory.CreateAs("author-c1");
        var path = $"/api/v1/decisions/{decisionId}/complaints";

        var filed = await author.PostAsync(path, DsaTestFactory.JsonBody(new { explanation = Explanation }));
        var repeated = await author.PostAsync(path, DsaTestFactory.JsonBody(new { explanation = Explanation }));

        Assert.Equal(HttpStatusCode.Created, filed.StatusCode);
        using var receipt = await filed.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("received", receipt!.RootElement.GetProperty("status").GetString());
        Assert.Equal(decisionId, receipt.RootElement.GetProperty("decisionId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
    }

    [Fact]
    public async Task Other_users_and_anonymous_callers_cannot_complain_about_someone_elses_decision()
    {
        var decisionId = await RejectReviewAsync("szkola-skarga-2", "author-c2");
        var path = $"/api/v1/decisions/{decisionId}/complaints";

        var stranger = await _factory.CreateAs("author-c2-other").PostAsync(path, DsaTestFactory.JsonBody(new { explanation = Explanation }));
        var anonymous = await _factory.CreateClient().PostAsync(path, DsaTestFactory.JsonBody(new { explanation = Explanation }));

        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Notifier_complains_with_the_notice_token_and_a_wrong_token_is_refused()
    {
        var review = DsaTestFactory.NewReview("szkola-skarga-3", "author-c3", "published");
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });

        var notifier = _factory.CreateClient();
        var noticeResponse = await notifier.PostAsync("/api/v1/notices", DsaTestFactory.JsonBody(new
        {
            reviewId = review.Id,
            explanation = "Opinia zawiera nazwisko dyrektora i obraźliwe sformułowania.",
            category = "defamation",
            goodFaithConfirmed = true,
        }));
        using var notice = await noticeResponse.Content.ReadFromJsonAsync<JsonDocument>();
        var noticeId = notice!.RootElement.GetProperty("id").GetGuid();
        var ackToken = notice.RootElement.GetProperty("ackToken").GetString()!;

        var decided = await _factory.CreateAs("mod-c3", true, "Admin").PostAsync(
            $"/api/v1/moderation/notices/{noticeId}/decision",
            DsaTestFactory.JsonBody(new { action = "remove", legalOrTermsGround = Ground, facts = Facts }));
        using var decision = await decided.Content.ReadFromJsonAsync<JsonDocument>();
        var decisionId = decision!.RootElement.GetProperty("id").GetGuid();
        var path = $"/api/v1/decisions/{decisionId}/complaints";

        var wrong = await notifier.PostAsync(path, DsaTestFactory.JsonBody(new { explanation = Explanation, token = new string('B', 22) }));
        var right = await notifier.PostAsync(path, DsaTestFactory.JsonBody(new { explanation = Explanation, token = ackToken }));

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Created, right.StatusCode);
    }

    [Fact]
    public async Task Moderator_records_the_complaint_outcome_once()
    {
        var decisionId = await RejectReviewAsync("szkola-skarga-4", "author-c4");
        var author = _factory.CreateAs("author-c4");
        var filed = await author.PostAsync($"/api/v1/decisions/{decisionId}/complaints", DsaTestFactory.JsonBody(new { explanation = Explanation }));
        using var receipt = await filed.Content.ReadFromJsonAsync<JsonDocument>();
        var complaintId = receipt!.RootElement.GetProperty("id").GetGuid();

        var moderator = _factory.CreateAs("mod-c4", true, "Admin");
        var path = $"/api/v1/moderation/complaints/{complaintId}/decision";
        var upheld = await moderator.PostAsync(path, DsaTestFactory.JsonBody(new { outcome = "upheld", facts = Facts }));
        var again = await moderator.PostAsync(path, DsaTestFactory.JsonBody(new { outcome = "reversed", facts = Facts }));

        Assert.Equal(HttpStatusCode.OK, upheld.StatusCode);
        using var outcome = await upheld.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.Equal("upheld", outcome!.RootElement.GetProperty("status").GetString());
        Assert.False(outcome.RootElement.GetProperty("outcome").GetProperty("automated").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    /// <summary>Creates a pending review by the author and rejects it as a moderator. Returns the decision id.</summary>
    private async Task<Guid> RejectReviewAsync(string slug, string authorSub)
    {
        var review = DsaTestFactory.NewReview(slug, authorSub);
        await _factory.UseDbAsync(db => { db.Reviews.Add(review); return db.SaveChangesAsync(); });

        var response = await _factory.CreateAs("mod-" + authorSub, true, "Admin").PostAsync(
            $"/api/v1/moderation/reviews/{review.Id}/decision",
            DsaTestFactory.JsonBody(new { action = "reject", legalOrTermsGround = Ground, facts = Facts }));
        response.EnsureSuccessStatusCode();
        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.GetProperty("id").GetGuid();
    }
}
