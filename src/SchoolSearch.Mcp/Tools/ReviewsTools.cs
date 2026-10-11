using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SchoolSearch.Mcp.Reviews;

namespace SchoolSearch.Mcp.Tools;

/// <summary>
/// The read-only reviews tool (opinie użytkowników). It forwards to one Reviews endpoint and returns the envelope
/// verbatim. The envelope is user-generated content (userGenerated: true) and is never a score input.
/// </summary>
[McpServerToolType]
public sealed class ReviewsTools(ReviewsClient reviews)
{
    private const string UnavailableCode = "reviews_unavailable";
    private const string UnavailableMessage = "School reviews are temporarily unavailable. Try again shortly.";

    [McpServerTool(Name = "get_school_reviews")]
    [Description("Returns one page of the published user-generated opinions (reviews) of one school, with the public replies of the school, given its slug (as returned by find_schools)." + ToolDescriptions.ReviewRules)]
    public async Task<CallToolResult> GetSchoolReviews(
        [Description("School slug, as returned by find_schools.")] string slug,
        [Description("Page number, starting at 1.")] int? page = null,
        [Description("Items per page, 1 to 50.")] int? pageSize = null,
        CancellationToken ct = default)
    {
        var cleaned = slug?.Trim() ?? string.Empty;
        if (cleaned.Length == 0)
        {
            return ToolResponse.Failure("validation_failed", "slug is required.");
        }

        if (!reviews.IsConfigured)
        {
            return ToolResponse.Failure(UnavailableCode, UnavailableMessage);
        }

        var queryString = ApiQuery.Build(
            ("page", ApiQuery.Integer(page is null ? null : Math.Max(1, page.Value))),
            ("pageSize", ApiQuery.Integer(pageSize is null ? null : Math.Clamp(pageSize.Value, 1, SchoolTools.MaxPageSize))));

        var response = await reviews.GetAsync(
            $"api/v1/schools/{Uri.EscapeDataString(cleaned)}/reviews" + queryString,
            ct);

        // An unreachable or failing Reviews service is one structured error. The Reviews service's own 4xx codes
        // (for example not_found for an unknown school) pass through as they are.
        return response.StatusCode >= 500
            ? ToolResponse.Failure(UnavailableCode, UnavailableMessage)
            : ToolResponse.From(response);
    }
}
