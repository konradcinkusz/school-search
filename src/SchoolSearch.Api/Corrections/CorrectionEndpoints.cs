using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Api.Errors;
using SchoolSearch.Api.RateLimiting;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Catalog.Data.Entities;

namespace SchoolSearch.Api.Corrections;

/// <summary>
/// Public correction requests about a school's published data. Anonymous. The write path is limited per IP
/// by the global limiter and flood-limited per client, school and field by <see cref="CorrectionFloodGuard"/>.
/// The API stores the text verbatim and never renders it. The message and contact email are never logged
/// and never echoed back. Only the random public token is exposed, never the internal Guid.
/// </summary>
public static class CorrectionEndpoints
{
    /// <summary>Open (new or in_review) requests one client may hold per school and field within 24 hours.</summary>
    public const int MaxOpenPerSchoolFieldPerClient = 3;

    private const int TokenBytes = 16;
    private const int TokenLength = 22;

    public static IEndpointRouteBuilder MapCorrectionEndpoints(this IEndpointRouteBuilder app)
    {
        var corrections = app.MapGroup("/api/v1/corrections");

        corrections.MapPost(string.Empty, CreateCorrection).WithName(CorrectionOperations.Create);
        corrections.MapGet("/{token}", GetCorrection).WithName(CorrectionOperations.GetStatus);

        return app;
    }

    /// <summary>
    /// Registers what the correction endpoints need: the memory cache behind the flood guard and the guard itself.
    /// Called from the composition root (Extensions/ServiceCollectionExtensions.cs).
    /// </summary>
    public static IServiceCollection AddCorrectionServices(this IServiceCollection services)
    {
        // The size limit bounds the guard's memory. Each entry is one client, school and field.
        services.AddMemoryCache(options => options.SizeLimit = 50_000);
        services.AddSingleton<CorrectionFloodGuard>();

        return services;
    }

    internal static async Task<IResult> CreateCorrection(
        CorrectionSubmission? body,
        HttpContext httpContext,
        ClientIpResolver clientIp,
        CatalogDbContext db,
        CorrectionFloodGuard guard,
        CancellationToken ct)
    {
        var validation = CorrectionRequestValidator.Validate(body ?? new CorrectionSubmission(null, null, null, null));
        if (validation.Value is not { } input)
        {
            return ApiErrorWriter.Result(StatusCodes.Status400BadRequest, "validation_failed", validation.Error ?? "Request is invalid.");
        }

        // A slug longer than any stored slug cannot match a record. It gets the same answer as any unknown slug.
        var targetId = await ResolveTargetIdAsync(db, input, ct);
        if (targetId is not { } target)
        {
            return TargetNotFound(input.Kind);
        }

        var storedField = CorrectionStorage.StoredField(input.Kind, input.Field);
        var now = DateTimeOffset.UtcNow;
        var clientKey = guard.KeyFor(clientIp.Resolve(httpContext), target, storedField);

        // One process-wide gate: the count check and the insert must not interleave between two submissions.
        await guard.Gate.WaitAsync(ct);
        try
        {
            var recent = guard.Recall(clientKey, now);
            if (recent.Count > 0)
            {
                var recentIds = recent.Select(r => r.Id).ToList();
                var stillOpen = await db.CorrectionRequests.AsNoTracking()
                    .Where(r => recentIds.Contains(r.Id) && (r.Status == CorrectionStatuses.New || r.Status == CorrectionStatuses.InReview))
                    .Select(r => r.Id)
                    .ToListAsync(ct);

                // Requests that were closed (accepted, rejected, duplicate) no longer count against the client.
                recent = recent.Where(r => stillOpen.Contains(r.Id)).ToList();
                if (recent.Count >= MaxOpenPerSchoolFieldPerClient)
                {
                    guard.Store(clientKey, recent);
                    return TooManyRequests(httpContext, recent, now);
                }
            }

            var entity = new CorrectionRequest
            {
                Id = Guid.NewGuid(),
                PublicToken = NewToken(),
                SchoolId = target,
                Field = storedField,
                Message = input.Message,
                ContactEmail = input.ContactEmail,
                Status = CorrectionStatuses.New,
                CreatedAt = now,
            };

            db.CorrectionRequests.Add(entity);
            await db.SaveChangesAsync(ct);

            recent.Add(new CorrectionFloodGuard.Submission(entity.Id, now));
            guard.Store(clientKey, recent);

            return TypedResults.Created(
                $"/api/v1/corrections/{entity.PublicToken}",
                new CorrectionCreatedResponse(entity.PublicToken, entity.Status));
        }
        finally
        {
            guard.Gate.Release();
        }
    }

    internal static async Task<IResult> GetCorrection(string token, CatalogDbContext db, CancellationToken ct)
    {
        // Malformed and unknown tokens get the same answer, so the shape of the token space is not revealed.
        if (!IsWellFormedToken(token))
        {
            return CorrectionNotFound();
        }

        var request = await db.CorrectionRequests.AsNoTracking()
            .Where(r => r.PublicToken == token)
            .Select(r => new { r.PublicToken, r.Status, r.StatusReason, r.CreatedAt, r.SchoolId, r.Field })
            .FirstOrDefaultAsync(ct);

        if (request is null)
        {
            return CorrectionNotFound();
        }

        var kind = CorrectionStorage.KindOf(request.Field);
        var targetSlug = await FindTargetSlugAsync(db, request.SchoolId, kind, ct);
        if (targetSlug is null)
        {
            return CorrectionNotFound();
        }

        return TypedResults.Ok(new CorrectionStatusResponse(
            request.PublicToken,
            request.Status,
            request.StatusReason,
            request.CreatedAt,
            targetSlug,
            CorrectionStorage.BareField(request.Field),
            kind));
    }

    /// <summary>The stored SchoolId for the target named by slug and kind, or null when no such record exists.</summary>
    private static async Task<long?> ResolveTargetIdAsync(CatalogDbContext db, NormalizedCorrection input, CancellationToken ct)
    {
        if (input.Slug.Length > CorrectionRequestValidator.MaxSlugLength)
        {
            return null;
        }

        if (input.Kind == CorrectionTargetKinds.Preschool)
        {
            var preschoolId = await db.Preschools.AsNoTracking()
                .Where(p => p.Slug == input.Slug)
                .Select(p => (long?)p.IdPodmiot)
                .FirstOrDefaultAsync(ct);

            return preschoolId is { } id ? CorrectionStorage.StoredTargetId(CorrectionTargetKinds.Preschool, id) : null;
        }

        return await FindSchoolIdAsync(db, input.Slug, ct);
    }

    /// <summary>The public slug of a stored target. A preschool row is looked up in Preschools, never in Schools.</summary>
    private static async Task<string?> FindTargetSlugAsync(CatalogDbContext db, long storedTargetId, string kind, CancellationToken ct)
    {
        if (kind == CorrectionTargetKinds.Preschool)
        {
            var preschoolId = CorrectionStorage.PreschoolIdOf(storedTargetId);
            return await db.Preschools.AsNoTracking()
                .Where(p => p.IdPodmiot == preschoolId)
                .Select(p => p.Slug)
                .FirstOrDefaultAsync(ct);
        }

        return await db.Schools.AsNoTracking()
            .Where(s => s.IdPodmiot == storedTargetId)
            .Select(s => s.Slug)
            .FirstOrDefaultAsync(ct);
    }

    private static Task<long?> FindSchoolIdAsync(CatalogDbContext db, string slug, CancellationToken ct) =>
        db.Schools.AsNoTracking()
            .Where(s => s.Slug == slug)
            .Select(s => (long?)s.IdPodmiot)
            .FirstOrDefaultAsync(ct);

    private static IResult TargetNotFound(string kind) =>
        kind == CorrectionTargetKinds.Preschool
            ? ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Preschool not found.")
            : ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "School not found.");

    private static IResult CorrectionNotFound() =>
        ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Correction request not found.");

    /// <summary>Same uniform body as the global limiter, plus Retry-After: the time until the oldest counted request leaves the 24-hour window.</summary>
    private static IResult TooManyRequests(HttpContext httpContext, IReadOnlyList<CorrectionFloodGuard.Submission> open, DateTimeOffset now)
    {
        var oldest = open.Min(s => s.At);
        var seconds = Math.Max(1, (int)Math.Ceiling((oldest + CorrectionFloodGuard.Window - now).TotalSeconds));
        httpContext.Response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);

        return ApiErrorWriter.Result(StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Retry later.");
    }

    /// <summary>16 bytes from the CSPRNG, base64url without padding: 22 characters, 128 bits. Never a GUID.</summary>
    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool IsWellFormedToken(string token)
    {
        if (token.Length != TokenLength)
        {
            return false;
        }

        foreach (var c in token)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Stable operation names (generated clients and OpenAPI).</summary>
public static class CorrectionOperations
{
    public const string Create = "CreateCorrection";
    public const string GetStatus = "GetCorrectionStatus";
}

/// <summary>Values stored in CorrectionRequest.Status. Public statuses are the same strings the API returns.</summary>
public static class CorrectionStatuses
{
    public const string New = "new";
    public const string InReview = "in_review";
}

/// <summary>201 body of POST /api/v1/corrections. The token is the only handle the submitter gets.</summary>
public sealed record CorrectionCreatedResponse(string Token, string Status);

/// <summary>
/// Body of GET /api/v1/corrections/{token}. No message, no contact email, no internal id. SchoolSlug is the slug of the
/// corrected record, school or preschool (see Kind); Field is the bare field code.
/// </summary>
public sealed record CorrectionStatusResponse(
    string Token,
    string Status,
    string? StatusReason,
    DateTimeOffset CreatedAt,
    string SchoolSlug,
    string Field,
    string Kind);

/// <summary>
/// Limits open correction requests per client, school and field over 24 hours. The client is identified by a
/// salted SHA-256 of its IP. The IP itself is never stored, and nothing about the client is written to the table.
/// State lives in IMemoryCache with sliding expiry, so it resets on restart. Each process draws its own random
/// salt, so the keys are meaningless outside the process.
/// </summary>
public sealed class CorrectionFloodGuard(IMemoryCache cache)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(32);

    /// <summary>Serializes the check-then-insert sequence for correction submissions.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>A counted request: its internal id and when it was created. Internal only, never returned.</summary>
    public sealed record Submission(Guid Id, DateTimeOffset At);

    public string KeyFor(string clientAddress, long schoolId, string field)
    {
        var ip = Encoding.UTF8.GetBytes(clientAddress);
        var buffer = new byte[_salt.Length + ip.Length];
        _salt.CopyTo(buffer, 0);
        ip.CopyTo(buffer, _salt.Length);

        var digest = Convert.ToHexString(SHA256.HashData(buffer));
        return $"corr:{digest}:{schoolId}:{field}";
    }

    /// <summary>Returns a mutable copy of the submissions still inside the 24-hour window.</summary>
    public List<Submission> Recall(string key, DateTimeOffset now)
    {
        if (!cache.TryGetValue<List<Submission>>(key, out var stored) || stored is null)
        {
            return [];
        }

        return stored.Where(s => now - s.At < Window).ToList();
    }

    public void Store(string key, List<Submission> submissions)
    {
        if (submissions.Count == 0)
        {
            cache.Remove(key);
            return;
        }

        cache.Set(key, submissions, new MemoryCacheEntryOptions
        {
            SlidingExpiration = Window,
            Size = 1,
        });
    }
}
