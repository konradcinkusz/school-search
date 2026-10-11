using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SchoolSearch.Reviews.Data;

namespace SchoolSearch.Reviews.Tests;

/// <summary>DSA test helpers on top of the shared <see cref="ReviewsApiFactory"/> (identity comes from TestAuthHandler headers).</summary>
internal static class DsaTestSupport
{
    public const string ContactEmail = "kontakt@example.test";

    /// <summary>A client whose requests carry the given identity. Roles are authservice role names (Admin, SuperAdmin).</summary>
    public static HttpClient AsUser(this ReviewsApiFactory factory, string sub, bool verified = true, params string[] roles)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailVerifiedHeader, verified ? "true" : "false");
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RoleHeader, string.Join(',', roles));
        }

        return client;
    }

    public static JsonContent JsonBody(object value) =>
        JsonContent.Create(value, options: new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static async Task UseDbAsync(this ReviewsApiFactory factory, Func<ReviewsDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        await db.Database.EnsureCreatedAsync();
        await action(db);
    }

    public static async Task<T> ReadDbAsync<T>(this ReviewsApiFactory factory, Func<ReviewsDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        return await query(db);
    }

    public const string ReviewBody = "Opinia testowa o szkole, wystarczajaco dluga do walidacji tresci.";

    public static Review NewReview(ReviewsApiFactory factory, string schoolSlug, string authorSub, string status = "pending") => new()
    {
        Id = Guid.NewGuid(),
        SchoolSlug = schoolSlug,
        AuthorHash = factory.HashFor(authorSub),
        Rating = 4,
        Title = "Tytul opinii",
        Body = ReviewBody,
        SchoolYearFrom = 2025,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-Random.Shared.Next(1, 10_000)),
        UpdatedAt = DateTimeOffset.UtcNow,
        PublishedAt = status == "published" ? DateTimeOffset.UtcNow : null,
        TermsVersion = "draft-1",
    };
}
