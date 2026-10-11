using SchoolSearch.Reviews.Models;

namespace SchoolSearch.Reviews.Endpoints;

/// <summary>
/// Input rules for reviews and replies. Returns the trimmed draft or the first problem in English (the Polish UI maps codes).
/// </summary>
public static class ReviewValidator
{
    public const int MaxSlugLength = 200;
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxTitleLength = 120;
    public const int MinBodyLength = 40;
    public const int MaxBodyLength = 4000;
    public const int FirstSchoolYear = 2000;
    public const int MinReplyLength = 10;
    public const int MaxReplyLength = 2000;
    public const int MaxLabelLength = 120;

    /// <summary>The Polish school year starts on 1 September: from September the new year counts as current.</summary>
    public const int SchoolYearStartMonth = 9;

    public static string? ValidateSlug(string slug) =>
        string.IsNullOrWhiteSpace(slug) || slug.Length > MaxSlugLength
            ? "School slug is missing or too long."
            : null;

    public static (ReviewDraft? Draft, string? Error) ValidateReview(CreateReviewRequest request, DateTimeOffset now)
    {
        if (request.Rating is < MinRating or > MaxRating)
        {
            return (null, "Rating must be between 1 and 5.");
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length is 0 || title.Length > MaxTitleLength)
        {
            return (null, "Title must be 1 to 120 characters.");
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length < MinBodyLength || body.Length > MaxBodyLength)
        {
            return (null, "Body must be 40 to 4000 characters.");
        }

        var latestSchoolYear = CurrentSchoolYearStart(now);
        if (request.SchoolYearFrom < FirstSchoolYear || request.SchoolYearFrom > latestSchoolYear)
        {
            return (null, $"School year must start between {FirstSchoolYear} and {latestSchoolYear}.");
        }

        return (new ReviewDraft(request.Rating, title, body, request.SchoolYearFrom), null);
    }

    public static (ReplyDraft? Draft, string? Error) ValidateReply(ReviewReplyRequest request)
    {
        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length < MinReplyLength || body.Length > MaxReplyLength)
        {
            return (null, "Reply must be 10 to 2000 characters.");
        }

        var label = request.RepresentativeLabel?.Trim() ?? string.Empty;
        if (label.Length is 0 || label.Length > MaxLabelLength)
        {
            return (null, "Representative label must be 1 to 120 characters.");
        }

        return (new ReplyDraft(body, label), null);
    }

    /// <summary>First year of the school year that is current at <paramref name="now"/>.</summary>
    public static int CurrentSchoolYearStart(DateTimeOffset now) =>
        now.Month >= SchoolYearStartMonth ? now.Year : now.Year - 1;
}
