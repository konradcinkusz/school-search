namespace SchoolSearch.Mcp.Catalog;

/// <summary>
/// The only way the MCP server reads the catalog: GET requests against the Api's public JSON contract. Responses are
/// returned as raw bodies so the Api JSON reaches the model verbatim. Never logs the body or the key.
/// </summary>
public sealed class CatalogClient(HttpClient http)
{
    /// <summary>Logical name of the typed client ("catalog"). Used for the registration and in test overrides.</summary>
    public const string HttpClientName = "catalog";

    public async Task<CatalogResponse> GetAsync(string relativePath, CancellationToken ct)
    {
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

/// <summary>Status code and raw body of one catalog call. Status 0 is never sent; <see cref="Unavailable"/> uses 503.</summary>
public sealed record CatalogResponse(int StatusCode, string Body)
{
    public static CatalogResponse Unavailable { get; } = new(503, string.Empty);

    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
