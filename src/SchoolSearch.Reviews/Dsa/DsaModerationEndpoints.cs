using System.Security.Claims;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>
/// Moderator routes under /api/v1/moderation. The group requires the Moderator policy (role Admin or SuperAdmin).
/// The moderator's subject is stored only as a hash, in ModerationDecision.DecidedBy.
/// </summary>
internal static class DsaModerationEndpoints
{
    internal static async Task<IResult> GetQueue(
        string? status,
        string? page,
        string? pageSize,
        DsaModerationService moderation,
        CancellationToken ct)
    {
        var filter = string.IsNullOrWhiteSpace(status) ? "open" : status.Trim().ToLowerInvariant();
        if (filter is not ("open" or "pending" or "received"))
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", "Unknown status.");
        }

        var (pageNumber, size) = DsaHttp.ClampPaging(page, pageSize);
        var result = await moderation.QueueAsync(filter, pageNumber, size, ct);
        return TypedResults.Ok(result);
    }

    internal static async Task<IResult> DecideReview(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        DsaSubjectHasher hasher,
        DsaModerationService moderation,
        CancellationToken ct)
    {
        if (Precondition(user, hasher, out var subject) is { } refusal)
        {
            return refusal;
        }

        var (body, error) = await DsaHttp.ReadBodyAsync<DsaDecisionRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var validation = DsaValidation.ValidateDecision(body, DsaValidation.ReviewActions);
        if (validation.Value is not { } input)
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        return Respond(await moderation.DecideReviewAsync(id, subject, input, ct));
    }

    internal static async Task<IResult> DecideReply(
        Guid reviewId,
        HttpRequest request,
        ClaimsPrincipal user,
        DsaSubjectHasher hasher,
        DsaModerationService moderation,
        CancellationToken ct)
    {
        if (Precondition(user, hasher, out var subject) is { } refusal)
        {
            return refusal;
        }

        var (body, error) = await DsaHttp.ReadBodyAsync<DsaDecisionRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var validation = DsaValidation.ValidateDecision(body, DsaValidation.ReplyActions);
        if (validation.Value is not { } input)
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        return Respond(await moderation.DecideReplyAsync(reviewId, subject, input, ct));
    }

    internal static async Task<IResult> DecideNotice(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        DsaSubjectHasher hasher,
        DsaModerationService moderation,
        CancellationToken ct)
    {
        if (Precondition(user, hasher, out var subject) is { } refusal)
        {
            return refusal;
        }

        var (body, error) = await DsaHttp.ReadBodyAsync<DsaDecisionRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var validation = DsaValidation.ValidateDecision(body, DsaValidation.NoticeActions);
        if (validation.Value is not { } input)
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        return Respond(await moderation.DecideNoticeAsync(id, subject, input, ct));
    }

    internal static async Task<IResult> DecideComplaint(
        Guid id,
        HttpRequest request,
        ClaimsPrincipal user,
        DsaSubjectHasher hasher,
        DsaModerationService moderation,
        CancellationToken ct)
    {
        if (Precondition(user, hasher, out var subject) is { } refusal)
        {
            return refusal;
        }

        var (body, error) = await DsaHttp.ReadBodyAsync<DsaComplaintDecisionRequest>(request, ct);
        if (body is null)
        {
            return error!;
        }

        var validation = DsaValidation.ValidateComplaintDecision(body);
        if (validation.Value is not { } input)
        {
            return DsaHttp.Error(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        return Respond(await moderation.DecideComplaintAsync(id, subject, input, ct));
    }

    /// <summary>Null when the moderator can act. Sets the subject for hashing.</summary>
    private static IResult? Precondition(ClaimsPrincipal user, DsaSubjectHasher hasher, out string subject)
    {
        subject = user.FindFirstValue("sub") ?? string.Empty;
        if (subject.Length == 0)
        {
            return DsaHttp.Error(StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required.");
        }

        if (!hasher.IsConfigured)
        {
            return DsaHttp.Error(StatusCodes.Status503ServiceUnavailable, "pepper_not_configured", "Reviews subject pepper is not configured.");
        }

        return null;
    }

    private static IResult Respond<T>(DsaResult<T> result) where T : class
    {
        if (result.Value is { } value)
        {
            return TypedResults.Ok(value);
        }

        return DsaHttp.Failure(result.Failure!);
    }
}
