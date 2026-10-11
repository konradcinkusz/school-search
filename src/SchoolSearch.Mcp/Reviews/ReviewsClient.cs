using SchoolSearch.Mcp.Catalog;

namespace SchoolSearch.Mcp.Reviews;

/// <summary>
/// The MCP server's read path to the Reviews service: anonymous GET requests only. Unlike the catalog, the Reviews
/// base address is optional. When it is not configured, <see cref="IsConfigured"/> is false and the tool answers a
/// structured reviews_unavailable error without calling out. Never sends a bearer token or the catalog key.
/// </summary>
public sealed class ReviewsClient(HttpClient http)
{
    /// <summary>Logical name of the typed client ("reviews"). Used for the registration and in test overrides.</summary>
    public const string HttpClientName = "reviews";

    public bool IsConfigured => http.BaseAddress is not null;

    public async Task<CatalogResponse> GetAsync(string relativePath, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return CatalogResponse.Unavailable;
        }

        try
        {
            using var response = await http.GetAsync(relativePath, ct);
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
}
