using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolSearch.Reviews.Ai;
using SchoolSearch.Reviews.Data;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Reviews.Tests;

/// <summary>Stands in for the model. Records every prompt and answers with <see cref="Respond"/>; no network.</summary>
public sealed class FakeReviewTriageClient : IReviewTriageClient
{
    private readonly object _gate = new();
    private readonly List<(string System, string User)> _prompts = new();
    private int _calls;

    public Func<string, ReviewTriageCompletion> Respond { get; set; } =
        _ => new ReviewTriageCompletion("{\"label\":\"ok\",\"note\":\"no sign of a problem\"}", 120, 30);

    /// <summary>When set, the call fails with this exception instead of answering.</summary>
    public Exception? ThrowOnCall { get; set; }

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _calls;
            }
        }
    }

    public IReadOnlyList<(string System, string User)> Prompts
    {
        get
        {
            lock (_gate)
            {
                return _prompts.ToArray();
            }
        }
    }

    public Task<ReviewTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        lock (_gate)
        {
            _calls++;
            _prompts.Add((system, user));
        }

        if (ThrowOnCall is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(Respond(user));
    }
}

/// <summary>A clock the test moves by hand. Used for the day rollover and the one-minute window.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class AiTriageTestSupport
{
    public static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public static ReviewsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseInMemoryDatabase("reviews-ai-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new ReviewsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    public static Review NewReview(
        string status = ReviewTriageService.PendingStatus,
        string title = "Dobra szkola",
        string body = "Nauczyciele są uważni, a świetlica działa po lekcjach do godziny siedemnastej.",
        string? authorHash = null,
        string? schoolSlug = null) => new()
        {
            Id = Guid.NewGuid(),
            SchoolSlug = schoolSlug ?? "szkola-podstawowa-1",
            AuthorHash = authorHash ?? new string('a', 64),
            Rating = 4,
            Title = title,
            Body = body,
            SchoolYearFrom = 2025,
            Status = status,
            CreatedAt = Start,
            UpdatedAt = Start,
            PublishedAt = status == "published" ? Start : null,
            TermsVersion = "2026-01-01",
        };

    public static ReviewTriageService CreateService(
        ReviewsDbContext db,
        FakeReviewTriageClient client,
        ManualClock clock,
        IntegrationStatusRegistry? registry = null,
        AiTriageOptions? settings = null,
        bool available = true)
    {
        var options = Options.Create(settings ?? new AiTriageOptions());
        return new ReviewTriageService(
            db,
            client,
            new ReviewTriageTokenBudget(clock, options),
            new ReviewTriageRateLimiter(clock, options),
            new AiTriageAvailability(available, available ? "configured" : "ANTHROPIC_API_KEY is not set; AI review triage is disabled"),
            options,
            registry ?? new IntegrationStatusRegistry(),
            clock,
            NullLogger<ReviewTriageService>.Instance);
    }
}
