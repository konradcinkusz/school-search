using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Tests;

/// <summary>Builders shared by the Reviews tests. Other test files (Dsa*, Ai*) use these, so keep the signatures stable.</summary>
internal static class ReviewFixtures
{
    public const string DefaultSlug = "szkola-testowa-1";

    public const string ValidBodyText =
        "Dziecko czuje się w tej szkole bezpiecznie, a nauczyciele są zaangażowani w pracę z klasą.";

    /// <summary>A review entity. Published reviews get PublishedAt; every other status leaves it null.</summary>
    public static Review NewReview(string slug, string status, string authorHash, int schoolYearFrom = 2025)
    {
        var now = DateTimeOffset.UtcNow;
        return new Review
        {
            Id = Guid.NewGuid(),
            SchoolSlug = slug,
            AuthorHash = authorHash,
            Rating = 4,
            Title = "Dobra szkoła",
            Body = ValidBodyText,
            SchoolYearFrom = schoolYearFrom,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            PublishedAt = status == ReviewStatuses.Published ? now : null,
            TermsVersion = ReviewsApiFactory.TermsVersion,
        };
    }

    /// <summary>A reply entity for the given review. The author hash is a fixed placeholder.</summary>
    public static ReviewReply NewReply(Guid reviewId, string status, string representativeLabel = "Dyrekcja szkoły") => new()
    {
        ReviewId = reviewId,
        Body = "Odpowiedź szkoły na opinię rodzica, przygotowana w ramach prawa do odpowiedzi.",
        RepresentativeLabel = representativeLabel,
        VerificationState = VerificationStates.Unverified,
        AuthorHash = new string('a', 64),
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>JSON body for POST /api/v1/schools/{slug}/reviews.</summary>
    public static object ValidReviewBody(int schoolYearFrom = 2025, string terms = ReviewsApiFactory.TermsVersion) => new
    {
        rating = 4,
        title = "Dobra szkoła",
        body = ValidBodyText,
        schoolYearFrom,
        acceptedTermsVersion = terms,
    };

    /// <summary>JSON body for POST /api/v1/reviews/{id}/reply.</summary>
    public static object ValidReplyBody() => new
    {
        body = "Dziękujemy za opinię, wyjaśniamy sprawę z wychowawcą klasy.",
        representativeLabel = "Dyrekcja szkoły",
    };

    /// <summary>
    /// Builds a request. A non-null <paramref name="sub"/> sends the test identity headers; null sends an anonymous request.
    /// </summary>
    public static HttpRequestMessage Request(
        HttpMethod method,
        string url,
        string? sub = null,
        bool verified = true,
        string? role = null,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (sub is not null)
        {
            request.Headers.Add(TestAuthHandler.SubHeader, sub);
            request.Headers.Add(TestAuthHandler.EmailVerifiedHeader, verified ? "true" : "false");
            if (role is not null)
            {
                request.Headers.Add(TestAuthHandler.RoleHeader, role);
            }
        }

        return request;
    }
}
