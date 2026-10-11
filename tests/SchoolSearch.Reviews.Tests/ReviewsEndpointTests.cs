using System.Net;
using System.Text;
using System.Text.Json;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.Models;
using static SchoolSearch.Reviews.Tests.JsonTestHelpers;
using static SchoolSearch.Reviews.Tests.ReviewFixtures;

namespace SchoolSearch.Reviews.Tests;

public sealed class ReviewsEndpointTests
{
    private const string Slug = DefaultSlug;
    private const string AuthorA = "author-a";
    private const string AuthorB = "author-b";
    private const string ReplyAuthor = "reply-author";

    [Fact]
    public async Task Public_list_returns_only_published_reviews_in_the_user_generated_envelope()
    {
        using var factory = new ReviewsApiFactory();
        await factory.SeedAsync(
            NewReview(Slug, ReviewStatuses.Published, factory.HashFor("u1"), 2024),
            NewReview(Slug, ReviewStatuses.Pending, factory.HashFor("u2"), 2024),
            NewReview(Slug, ReviewStatuses.Rejected, factory.HashFor("u3"), 2024),
            NewReview(Slug, ReviewStatuses.Removed, factory.HashFor("u4"), 2024),
            NewReview("inna-szkola", ReviewStatuses.Published, factory.HashFor("u5"), 2024));

        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/schools/{Slug}/reviews");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.True(root.GetProperty("userGenerated").GetBoolean());
        Assert.Equal(ReviewsPublicCopy.UserGeneratedNotice, root.GetProperty("notice").GetString());
        Assert.Equal(1, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32());

        var items = root.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var item = items[0];
        Assert.Equal(4, item.GetProperty("rating").GetInt32());
        Assert.Equal("2024/25", item.GetProperty("schoolYear").GetString());
        Assert.Equal(ReviewsPublicCopy.AuthorLabel, item.GetProperty("authorLabel").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("reply").ValueKind);
    }

    [Fact]
    public async Task Public_list_hides_a_pending_reply_and_shows_a_published_one()
    {
        using var factory = new ReviewsApiFactory();
        var withPendingReply = NewReview(Slug, ReviewStatuses.Published, factory.HashFor("u1"), 2024);
        var pendingReply = NewReply(withPendingReply.Id, ReplyStatuses.Pending);
        pendingReply.Body = "PENDING-REPLY-TEXT-NOT-PUBLIC-000000000000000";
        withPendingReply.Reply = pendingReply;

        var withPublishedReply = NewReview(Slug, ReviewStatuses.Published, factory.HashFor("u2"), 2023);
        var publishedReply = NewReply(withPublishedReply.Id, ReplyStatuses.Published, "Dyrekcja szkoły");
        withPublishedReply.Reply = publishedReply;
        await factory.SeedAsync(withPendingReply, withPublishedReply);

        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/v1/schools/{Slug}/reviews");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("PENDING-REPLY-TEXT-NOT-PUBLIC", raw);
        using var document = JsonDocument.Parse(raw);
        var replies = document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("reply"))
            .Where(reply => reply.ValueKind == JsonValueKind.Object)
            .ToList();

        var reply = Assert.Single(replies);
        Assert.Equal("Dyrekcja szkoły", reply.GetProperty("representativeLabel").GetString());
        Assert.Equal("unverified", reply.GetProperty("verificationState").GetString());
    }

    [Fact]
    public async Task Public_list_answers_for_an_unknown_school_with_an_empty_envelope()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/schools/nieznana/reviews?page=abc&pageSize=9999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await ReadJsonAsync(response);
        Assert.Equal(0, root.GetProperty("total").GetInt32());
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(50, root.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Create_without_a_token_answers_401()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync($"/api/v1/schools/{Slug}/reviews", JsonBody(ValidReviewBody()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_with_an_unverified_e_mail_answers_403()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA, verified: false,
            content: JsonBody(ValidReviewBody()));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_not_verified", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_stores_a_pending_review_under_a_hashed_author_and_keeps_it_out_of_the_public_list()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var created = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody())));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receipt = await ReadJsonAsync(created);
        Assert.Equal("pending", receipt.GetProperty("status").GetString());
        var id = receipt.GetProperty("id").GetGuid();

        using var list = await client.GetAsync($"/api/v1/schools/{Slug}/reviews");
        Assert.Equal(0, (await ReadJsonAsync(list)).GetProperty("total").GetInt32());

        var stored = await factory.FindReviewAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(factory.HashFor(AuthorA), stored!.AuthorHash);
        Assert.Equal(64, stored.AuthorHash.Length);
        Assert.DoesNotContain(AuthorA, stored.AuthorHash);
        Assert.Equal(ReviewsApiFactory.TermsVersion, stored.TermsVersion);
        Assert.Equal(ReviewStatuses.Pending, stored.Status);
    }

    [Fact]
    public async Task Create_twice_for_the_same_school_and_year_answers_409()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var first = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody(2025))));
        using var second = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody(2025))));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("duplicate_review", await ErrorCodeAsync(second));
    }

    [Fact]
    public async Task Create_with_invalid_input_answers_400()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        var badRating = new { rating = 0, title = "Tytuł", body = ValidBodyText, schoolYearFrom = 2025, acceptedTermsVersion = ReviewsApiFactory.TermsVersion };
        var shortBody = new { rating = 4, title = "Tytuł", body = "za krótko", schoolYearFrom = 2025, acceptedTermsVersion = ReviewsApiFactory.TermsVersion };
        var futureYear = new { rating = 4, title = "Tytuł", body = ValidBodyText, schoolYearFrom = 2999, acceptedTermsVersion = ReviewsApiFactory.TermsVersion };

        foreach (var body in new object[] { badRating, shortBody, futureYear })
        {
            using var response = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
                content: JsonBody(body)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation_failed", await ErrorCodeAsync(response));
        }
    }

    [Fact]
    public async Task Create_with_a_stale_terms_version_answers_400_terms_not_accepted()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody(terms: "draft-0"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("terms_not_accepted", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Create_with_a_malformed_body_answers_400_without_leaking_the_parser_message()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();
        var malformed = new StringContent("{ not json", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: malformed));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_body", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Body_over_the_16_kb_limit_answers_413()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();
        var oversized = new StringContent(new string('x', 17 * 1024), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: oversized));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("payload_too_large", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Mine_lists_only_the_callers_own_reviews_with_their_status()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();
        using var created = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody())));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var mine = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/reviews/mine", AuthorA));
        using var others = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/reviews/mine", AuthorB));

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var items = (await ReadJsonAsync(mine)).GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("pending", items[0].GetProperty("status").GetString());
        Assert.Equal("2025/26", items[0].GetProperty("schoolYear").GetString());
        Assert.Equal(0, (await ReadJsonAsync(others)).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Mine_requires_authentication()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/reviews/mine");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_is_allowed_for_the_author_only_and_marks_the_review_removed()
    {
        using var factory = new ReviewsApiFactory();
        var review = NewReview(Slug, ReviewStatuses.Published, factory.HashFor(AuthorA));
        await factory.SeedAsync(review);
        using var client = factory.CreateClient();

        using var byOther = await client.SendAsync(Request(HttpMethod.Delete, $"/api/v1/reviews/{review.Id}", AuthorB));
        using var byAuthor = await client.SendAsync(Request(HttpMethod.Delete, $"/api/v1/reviews/{review.Id}", AuthorA));

        Assert.Equal(HttpStatusCode.NotFound, byOther.StatusCode);
        Assert.Equal("review_not_found", await ErrorCodeAsync(byOther));
        Assert.Equal(HttpStatusCode.OK, byAuthor.StatusCode);
        var stored = await factory.FindReviewAsync(review.Id);
        Assert.Equal(ReviewStatuses.Removed, stored!.Status);
    }

    [Fact]
    public async Task Reply_needs_a_published_review_and_an_unclaimed_slot_and_is_pending_until_moderated()
    {
        using var factory = new ReviewsApiFactory();
        var review = NewReview(Slug, ReviewStatuses.Published, factory.HashFor(AuthorA));
        var pending = NewReview(Slug, ReviewStatuses.Pending, factory.HashFor(AuthorB), 2024);
        await factory.SeedAsync(review, pending);
        using var client = factory.CreateClient();
        var replyUrl = $"/api/v1/reviews/{review.Id}/reply";

        using var onPending = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/reviews/{pending.Id}/reply", ReplyAuthor,
            content: JsonBody(ValidReplyBody())));
        using var byAuthor = await client.SendAsync(Request(HttpMethod.Post, replyUrl, AuthorA,
            content: JsonBody(ValidReplyBody())));
        using var unverified = await client.SendAsync(Request(HttpMethod.Post, replyUrl, ReplyAuthor, verified: false,
            content: JsonBody(ValidReplyBody())));
        using var created = await client.SendAsync(Request(HttpMethod.Post, replyUrl, ReplyAuthor,
            content: JsonBody(ValidReplyBody())));
        using var again = await client.SendAsync(Request(HttpMethod.Post, replyUrl, "someone-else",
            content: JsonBody(ValidReplyBody())));

        Assert.Equal(HttpStatusCode.NotFound, onPending.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byAuthor.StatusCode);
        Assert.Equal("own_review", await ErrorCodeAsync(byAuthor));
        Assert.Equal(HttpStatusCode.Forbidden, unverified.StatusCode);
        Assert.Equal("email_not_verified", await ErrorCodeAsync(unverified));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("pending", (await ReadJsonAsync(created)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("reply_exists", await ErrorCodeAsync(again));
    }

    [Fact]
    public async Task Without_authservice_configured_auth_gated_endpoints_answer_503_and_the_public_list_still_works()
    {
        using var factory = new ReviewsApiFactory(authConfigured: false);
        using var client = factory.CreateClient();

        using var list = await client.GetAsync($"/api/v1/schools/{Slug}/reviews");
        using var create = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody())));
        using var mine = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/reviews/mine", AuthorA));
        using var anonymousMine = await client.GetAsync("/api/v1/reviews/mine");

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Equal("auth_unavailable", await ErrorCodeAsync(create));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, mine.StatusCode);
        Assert.Equal("auth_unavailable", await ErrorCodeAsync(mine));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, anonymousMine.StatusCode);
    }

    [Fact]
    public async Task Writes_are_limited_to_ten_per_subject_per_hour()
    {
        using var factory = new ReviewsApiFactory();
        using var client = factory.CreateClient();

        for (var year = 2000; year < 2010; year++)
        {
            using var accepted = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
                content: JsonBody(ValidReviewBody(year))));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }

        using var limited = await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/schools/{Slug}/reviews", AuthorA,
            content: JsonBody(ValidReviewBody(2010))));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await ErrorCodeAsync(limited));
    }
}
