using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SchoolSearch.Mcp.Catalog;
using SchoolSearch.Mcp.Options;

namespace SchoolSearch.Mcp.Reviews;

/// <summary>
/// The MCP server's read path to the Reviews service: anonymous GET requests only. Unlike the catalog, the Reviews
/// base address is optional. When it is not configured, <see cref="IsConfigured"/> is false and the tool answers a
/// structured reviews_unavailable error without calling out. Never sends a bearer token or the catalog key.
/// When BFF_SHARED_SECRET is set, each call carries it, plus the calling visitor's address as X-Client-Ip when the MCP
/// request has one, so Reviews meters the visitor, not the MCP service. Without a visitor address the call is a trusted
/// service call with its own bucket.
/// </summary>
public sealed class ReviewsClient(HttpClient http, IHttpContextAccessor httpContext, IConfiguration configuration, IOptions<McpOptions> options)
{
    /// <summary>Logical name of the typed client ("reviews"). Used for the registration and in test overrides.</summary>
    public const string HttpClientName = "reviews";

    private const string SharedSecretKey = "BFF_SHARED_SECRET";
    private const string SecretHeader = "X-Bff-Secret";
    private const string ClientIpHeader = "X-Client-Ip";
    private const string FlyClientIpHeader = "Fly-Client-IP";

    public bool IsConfigured => http.BaseAddress is not null;

    public async Task<CatalogResponse> GetAsync(string relativePath, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return CatalogResponse.Unavailable;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            AddSharedSecretHeaders(request);
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return new CatalogResponse((int)response.StatusCode, body);
        }
        catch (HttpRequestException)
        {
            return CatalogResponse.Unavailable;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (cold start budget exceeded), not a caller cancellation.
            return CatalogResponse.Unavailable;
        }
    }

    /// <summary>Sends the shared secret (never logged) and, when known, the visitor address. Nothing is sent without a secret.</summary>
    private void AddSharedSecretHeaders(HttpRequestMessage request)
    {
        var secret = configuration[SharedSecretKey];
        if (string.IsNullOrWhiteSpace(secret))
        {
            return;
        }

        request.Headers.TryAddWithoutValidation(SecretHeader, secret);

        var visitor = ResolveVisitorAddress();
        if (visitor is not null)
        {
            request.Headers.TryAddWithoutValidation(ClientIpHeader, visitor);
        }
    }

    /// <summary>
    /// The address of the visitor whose request reached this tool: Fly-Client-IP behind the Fly edge (opt-in, as for the
    /// limiter), otherwise the socket peer. Null when the request context is not available, so the call is not attributed.
    /// </summary>
    private string? ResolveVisitorAddress()
    {
        var context = httpContext.HttpContext;
        if (context is null)
        {
            return null;
        }

        if (options.Value.TrustProxyClientIpHeader
            && context.Request.Headers.TryGetValue(FlyClientIpHeader, out var header))
        {
            var value = header.ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString();
    }
}
