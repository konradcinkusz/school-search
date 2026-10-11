using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Api.Errors;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Operator surface under /api/v1/admin. Every route passes <see cref="AdminKeyFilter"/>; the group is
/// answered with 404 when ADMIN_API_KEY is unset. Responses are never cached.
/// </summary>
public static class AdminEndpoints
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxStatusReasonLength = 500;

    private static readonly string[] AllStatuses = ["new", "in_review", "accepted", "rejected", "duplicate"];

    /// <summary>
    /// Allowed status moves. "new" is only ever the initial state; accepted, rejected and duplicate are terminal.
    /// Any move not listed here (including a move to the same status) is refused with 409.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Transitions = new Dictionary<string, string[]>
    {
        ["new"] = ["in_review", "accepted", "rejected", "duplicate"],
        ["in_review"] = ["accepted", "rejected", "duplicate"],
        ["accepted"] = [],
        ["rejected"] = [],
        ["duplicate"] = [],
    };

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin").AddEndpointFilter<AdminKeyFilter>();

        admin.MapGet("/corrections", ListCorrections).WithName("AdminListCorrections");
        admin.MapPatch("/corrections/{id:guid}", UpdateCorrection).WithName("AdminUpdateCorrection");
        admin.MapPost("/keys/{id:guid}/revoke", RevokeKey).WithName("AdminRevokeApiKey");

        return app;
    }

    internal static async Task<IResult> ListCorrections(
        string? status,
        string? page,
        string? pageSize,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var filter = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        if (filter is not null && !AllStatuses.Contains(filter))
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Unknown status.");
        }

        var (pageNumber, size) = ClampPaging(page, pageSize);

        IQueryable<CorrectionRequest> query = db.CorrectionRequests.AsNoTracking();
        if (filter is not null)
        {
            query = query.Where(c => c.Status == filter);
        }

        var total = await query.CountAsync(ct);

        var items = new List<AdminCorrectionItem>();
        var offset = (long)(pageNumber - 1) * size;
        if (offset < total)
        {
            var rows = await query
                .OrderByDescending(c => c.CreatedAt)
                .ThenBy(c => c.Id)
                .Skip((int)offset)
                .Take(size)
                .ToListAsync(ct);

            var schoolIds = rows.Select(r => r.SchoolId).Distinct().ToArray();
            var slugs = await db.Schools
                .AsNoTracking()
                .Where(s => schoolIds.Contains(s.IdPodmiot))
                .ToDictionaryAsync(s => s.IdPodmiot, s => s.Slug, ct);

            items = rows.Select(r => AdminCorrectionItem.From(r, slugs.GetValueOrDefault(r.SchoolId))).ToList();
        }

        return TypedResults.Ok(new AdminCorrectionPage(items, pageNumber, size, total));
    }

    internal static async Task<IResult> UpdateCorrection(
        Guid id,
        HttpRequest request,
        CatalogDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var (body, error) = await JsonBodyReader.ReadAsync<UpdateCorrectionRequest>(request, ct);
        if (error is not null)
        {
            return error;
        }

        var target = body!.Status?.Trim();
        if (target is null || !AllStatuses.Contains(target))
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", "Unknown status.");
        }

        var reason = string.IsNullOrWhiteSpace(body.StatusReason) ? null : body.StatusReason.Trim();
        if (reason is not null && reason.Length > MaxStatusReasonLength)
        {
            return ApiErrorWriter.Result(
                StatusCodes.Status400BadRequest,
                "validation_failed",
                $"Status reason must be at most {MaxStatusReasonLength} characters.");
        }

        var entity = await db.CorrectionRequests.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Correction not found.");
        }

        if (!Transitions.TryGetValue(entity.Status, out var allowed) || !allowed.Contains(target))
        {
            return ApiErrorWriter.Result(StatusCodes.Status409Conflict, "invalid_transition", "Status change is not allowed from the current status.");
        }

        entity.Status = target;
        entity.StatusReason = reason;
        entity.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        var slug = await db.Schools
            .AsNoTracking()
            .Where(s => s.IdPodmiot == entity.SchoolId)
            .Select(s => s.Slug)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(AdminCorrectionItem.From(entity, slug));
    }

    internal static async Task<IResult> RevokeKey(
        Guid id,
        CatalogDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null)
        {
            return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "API key not found.");
        }

        // Revoking twice keeps the first revocation time.
        if (key.RevokedAt is null)
        {
            key.RevokedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new RevokedApiKey(key.Id, key.RevokedAt));
    }

    private static (int Page, int Size) ClampPaging(string? page, string? pageSize)
    {
        var pageNumber = int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? Math.Max(1, p) : 1;
        var size = int.TryParse(pageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            ? Math.Clamp(s, 1, MaxPageSize)
            : DefaultPageSize;
        return (pageNumber, size);
    }
}

public sealed class UpdateCorrectionRequest
{
    public string? Status { get; set; }

    public string? StatusReason { get; set; }
}

public sealed record AdminCorrectionItem(
    Guid Id,
    long SchoolId,
    string? SchoolSlug,
    string Field,
    string Message,
    string? ContactEmail,
    string Status,
    string? StatusReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    internal static AdminCorrectionItem From(CorrectionRequest r, string? slug) =>
        new(r.Id, r.SchoolId, slug, r.Field, r.Message, r.ContactEmail, r.Status, r.StatusReason, r.CreatedAt, r.UpdatedAt);
}

public sealed record AdminCorrectionPage(IReadOnlyList<AdminCorrectionItem> Items, int Page, int PageSize, long Total);

public sealed record RevokedApiKey(Guid Id, DateTimeOffset? RevokedAt);
