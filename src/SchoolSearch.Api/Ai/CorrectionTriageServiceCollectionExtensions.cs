using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Wiring for correction triage. Must be called AFTER <c>AddAiServices</c>, which registers the shared
/// <see cref="AiAvailability"/>, <see cref="TokenBudget"/> and the memory cache this feature uses.
/// Without a key no SDK client is constructed and the endpoint answers 503 <c>ai_unavailable</c>.
/// </summary>
public static class CorrectionTriageServiceCollectionExtensions
{
    public static IServiceCollection AddCorrectionTriage(this IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = Clean(configuration[AiServiceCollectionExtensions.ApiKeyVariable]);
        if (apiKey is not null)
        {
            AnthropicCorrectionTriageClient.Register(services, apiKey);
        }
        else
        {
            services.TryAddSingleton<ICorrectionTriageClient, UnconfiguredCorrectionTriageClient>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<CorrectionTriageRateLimiter>();
        services.AddScoped<CorrectionTriageService>();

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
