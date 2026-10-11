using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.Mcp.Reviews;

public static class ReviewsClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ReviewsClient"/> as a typed client named "reviews". The base address is optional and comes
    /// from Reviews:BaseUrl or REVIEWS_BASE_URL. Unset means the reviews tool answers reviews_unavailable; the service
    /// still starts. The standard resilience handler from the kernel applies to this client. The request context is read
    /// through IHttpContextAccessor so the visitor address can be forwarded.
    /// </summary>
    public static IServiceCollection AddReviewsClient(this IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration["Reviews:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = configuration["REVIEWS_BASE_URL"];
        }

        Uri? root = null;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var trimmed = configured.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("REVIEWS_BASE_URL must be an absolute http or https URL.");
            }

            // A trailing slash keeps any base path; the relative paths used by the tools carry no leading slash.
            root = new Uri(trimmed.TrimEnd('/') + "/", UriKind.Absolute);
        }

        services.AddHttpContextAccessor();

        services.AddHttpClient<ReviewsClient>(ReviewsClient.HttpClientName, client =>
        {
            if (root is not null)
            {
                client.BaseAddress = root;
            }

            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        return services;
    }
}
