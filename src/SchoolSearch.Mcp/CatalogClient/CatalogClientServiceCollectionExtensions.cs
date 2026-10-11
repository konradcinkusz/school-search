using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.Mcp.Catalog;

public static class CatalogClientServiceCollectionExtensions
{
    public const string DefaultCatalogUrl = "http://localhost:8080";

    /// <summary>
    /// Registers <see cref="CatalogClient"/> as a typed client named "catalog". The base address comes from
    /// CATALOG_API_URL, the optional key from MCP_API_KEY (sent as X-Api-Key, never logged). The standard resilience
    /// handler from the kernel applies to this client; the 30 second timeout is the cold-start budget.
    /// </summary>
    public static IServiceCollection AddCatalogClient(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration["CATALOG_API_URL"];
        var catalogUrl = string.IsNullOrWhiteSpace(configured) ? DefaultCatalogUrl : configured.Trim();

        if (!Uri.TryCreate(catalogUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("CATALOG_API_URL must be an absolute http or https URL.");
        }

        // A trailing slash keeps any base path; the relative paths used by the tools carry no leading slash.
        var root = new Uri(catalogUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var apiKey = configuration["MCP_API_KEY"];

        services.AddHttpClient<CatalogClient>(CatalogClient.HttpClientName, client =>
        {
            client.BaseAddress = root;
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
            }
        });

        return services;
    }

}
