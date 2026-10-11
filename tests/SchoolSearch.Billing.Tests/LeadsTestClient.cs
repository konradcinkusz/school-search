using System.Net.Http.Json;

namespace SchoolSearch.Billing.Tests;

/// <summary>HTTP helpers for the Leads tests. Each client carries its own visitor address through the BFF headers.</summary>
public static class LeadsTestClient
{
    /// <summary>A client that presents the BFF secret and a client IP, so rate-limit buckets are per test.</summary>
    public static HttpClient Create(LeadsApiFactory factory, string clientIp)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Bff-Secret", LeadsApiFactory.BffSecret);
        client.DefaultRequestHeaders.Add("X-Client-Ip", clientIp);
        return client;
    }

    /// <summary>Sends a request as a test subject (or anonymously when sub is null). Body is serialised with lowercase names.</summary>
    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? sub = null,
        bool emailVerified = true,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (sub is not null)
        {
            request.Headers.Add(LeadsTestAuthHandler.SubHeader, sub);
            request.Headers.Add(LeadsTestAuthHandler.EmailVerifiedHeader, emailVerified ? "true" : "false");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }
}
