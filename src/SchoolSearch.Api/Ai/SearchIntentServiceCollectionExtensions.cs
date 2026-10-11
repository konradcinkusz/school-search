using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Wiring for search intent. Called from Program.cs after <see cref="AiServiceCollectionExtensions.AddAiServices"/>
/// (it adds that call itself when it is missing). Without a key the endpoint answers 503 and no SDK client is built (P8).
/// </summary>
public static class SearchIntentServiceCollectionExtensions
{
    public static IServiceCollection AddSearchIntent(this IServiceCollection services, IConfiguration configuration)
    {
        // The kill switch, the model, the daily budget and the availability decision are shared with explain.
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(AiAvailability)))
        {
            services.AddAiServices(configuration);
        }

        services.Configure<SearchIntentOptions>(configuration.GetSection(SearchIntentOptions.SectionName));

        var apiKey = Clean(configuration[AiServiceCollectionExtensions.ApiKeyVariable]);
        if (apiKey is not null)
        {
            AnthropicSearchIntentClient.Register(services, apiKey);
        }
        else
        {
            services.TryAddSingleton<ISearchIntentClient, UnconfiguredSearchIntentClient>();
        }

        services.AddSingleton<SearchIntentRateLimiter>();
        services.AddScoped<SearchIntentService>();

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
