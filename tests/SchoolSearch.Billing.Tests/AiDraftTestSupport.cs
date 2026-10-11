using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SchoolSearch.Billing.Ai;
using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.Tests;

/// <summary>Stands in for the model. Records every prompt and answers with <see cref="Respond"/>, given the call number; no network.</summary>
public sealed class FakeDraftClient : IDraftClient
{
    private readonly object _gate = new();
    private readonly List<(string System, string User)> _prompts = new();
    private int _calls;

    public Func<int, DraftCompletion> Respond { get; set; } = _ => new DraftCompletion(AiDraftTestSupport.ValidJson, 120, 30);

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

    public Task<DraftCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        int call;
        lock (_gate)
        {
            _calls++;
            call = _calls;
            _prompts.Add((system, user));
        }

        if (ThrowOnCall is { } failure)
        {
            throw failure;
        }

        return Task.FromResult(Respond(call));
    }
}

/// <summary>A clock the test moves by hand. Used for the day rollover and the entitlement period.</summary>
public sealed class DraftTestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal static class AiDraftTestSupport
{
    public static readonly DateTimeOffset Start = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    public const string Slug = "szkola-podstawowa-1";
    public static readonly string OwnerHash = new('a', 64);
    public static readonly string OtherOwnerHash = new('b', 64);

    public const string ValidJson =
        "{\"headline\":\"Szkoła z klasami sportowymi\",\"description\":\"Szkoła prowadzi zajęcia w klasach sportowych. Uczniowie korzystają z boiska szkolnego i sali gimnastycznej.\"}";

    /// <summary>The owner's facts. Digits here are the only numbers a draft may use.</summary>
    public static readonly string[] Facts = ["Klasy sportowe od 4 klasy", "Boisko szkolne i sala gimnastyczna"];

    public static BillingDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseInMemoryDatabase("billing-ai-" + Guid.NewGuid().ToString("N"))
            .Options;
        var db = new BillingDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>Adds an owner's claim and subscription. Pass nulls to leave a row out.</summary>
    public static async Task SeedEntitlementAsync(
        BillingDbContext db,
        string ownerHash,
        string claimStatus = "approved",
        string? subscriptionStatus = "active",
        DateTimeOffset? periodEnd = null)
    {
        db.SchoolClaims.Add(new SchoolClaim
        {
            Id = Guid.NewGuid(),
            SchoolSlug = Slug,
            OwnerHash = ownerHash,
            RoleDescription = "Dyrekcja",
            Status = claimStatus,
            CreatedAt = Start,
        });

        if (subscriptionStatus is not null)
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(),
                SchoolSlug = Slug,
                OwnerHash = ownerHash,
                PlanCode = "test-plan",
                Status = subscriptionStatus,
                CurrentPeriodEnd = periodEnd ?? Start.AddDays(30),
                CancelAtPeriodEnd = subscriptionStatus == "canceling",
                CreatedAt = Start,
                UpdatedAt = Start,
            });
        }

        await db.SaveChangesAsync();
    }

    public static ProfileDraftService CreateService(
        BillingDbContext db,
        FakeDraftClient client,
        DraftTestClock clock,
        AiDraftOptions? settings = null,
        bool available = true)
    {
        var options = Options.Create(settings ?? new AiDraftOptions());
        return new ProfileDraftService(
            db,
            client,
            new DraftTokenBudget(clock, options),
            new DraftRateLimiter(options),
            new DraftAvailability(available, available ? "configured" : "ANTHROPIC_API_KEY is not set; AI profile drafts are disabled"),
            clock,
            NullLogger<ProfileDraftService>.Instance);
    }

    public static ProfileDraftRequest ValidRequest() => new(Facts);
}
