using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SchoolSearch.Billing.Tests;

/// <summary>
/// Two gap-closure rules. (1) The provider webhook has its own generous per-address window, outside the general per-IP
/// window; every other path keeps the general limit. (2) In Production on the InMemory store, every write answers 503
/// storage_unavailable while reads and health keep working.
/// </summary>
public sealed class WebhookLimitAndStorageGuardTests
{
    private const string Secret = "webhook-limit-secret-not-real";
    private const string UnknownPath = "/api/v1/gap-test-not-a-route";

    [Fact]
    public async Task Webhook_has_its_own_window_so_the_general_per_ip_limit_does_not_block_it()
    {
        // General window: 2 per minute. Webhook window: 4 per minute.
        using var factory = new GapFactory("Testing", perIpPerMinute: 2, webhookPerIpPerMinute: 4);
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var payload = PaymentsTestSupport.Event("evt-gap-limit-" + i, "payment_succeeded", DateTimeOffset.UtcNow);
            using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload, Secret);
            statuses.Add(response.StatusCode);
        }

        // Three requests above the general window of two still go through; only the fifth exceeds the webhook window.
        Assert.All(statuses.Take(4), status => Assert.NotEqual(HttpStatusCode.TooManyRequests, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[4]);
    }

    [Fact]
    public async Task Other_paths_keep_the_general_per_ip_limit()
    {
        using var factory = new GapFactory("Testing", perIpPerMinute: 2, webhookPerIpPerMinute: 4);
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await client.GetAsync(UnknownPath);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        using var limited = await client.GetAsync(UnknownPath);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await PaymentsTestSupport.ErrorCodeAsync(limited));
    }

    [Fact]
    public async Task In_production_on_the_in_memory_store_a_webhook_post_answers_503_storage_unavailable()
    {
        using var factory = new GapFactory("Production", perIpPerMinute: 60, webhookPerIpPerMinute: 600);
        using var client = factory.CreateClient();
        var payload = PaymentsTestSupport.Event("evt-gap-storage-1", "payment_succeeded", DateTimeOffset.UtcNow);

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload, Secret);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("storage_unavailable", await PaymentsTestSupport.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task In_production_reads_and_health_keep_working_and_health_shows_the_database_degraded()
    {
        using var factory = new GapFactory("Production", perIpPerMinute: 60, webhookPerIpPerMinute: 600);
        using var client = factory.CreateClient();

        using var read = await client.GetAsync(UnknownPath);
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, read.StatusCode);

        using var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var document = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        var database = document.RootElement.GetProperty("integrations").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == "database");
        Assert.Equal("degraded", database.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Outside_production_the_in_memory_store_still_accepts_webhook_posts()
    {
        using var factory = new GapFactory("Testing", perIpPerMinute: 60, webhookPerIpPerMinute: 600);
        using var client = factory.CreateClient();
        var payload = PaymentsTestSupport.Event("evt-gap-testing-1", "payment_succeeded", DateTimeOffset.UtcNow);

        using var response = await PaymentsTestSupport.PostWebhookAsync(client, payload, Secret);

        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

/// <summary>Hosts the real Billing service with the given environment and rate-limit settings (InMemory store, a test secret).</summary>
internal sealed class GapFactory(string environment, int perIpPerMinute, int webhookPerIpPerMinute) : WebApplicationFactory<Program>
{
    public const string Secret = "webhook-limit-secret-not-real";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("DATABASE_PROVIDER", "InMemory");
        builder.UseSetting("Billing:SubjectPepper", "gap-test-pepper");
        builder.UseSetting("Billing:MockMode", "false");
        builder.UseSetting("Billing:WebhookSecret", Secret);
        builder.UseSetting("Billing:RateLimiting:PerIpPerMinute", perIpPerMinute.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Billing:RateLimiting:WebhookPerIpPerMinute", webhookPerIpPerMinute.ToString(CultureInfo.InvariantCulture));
    }
}
