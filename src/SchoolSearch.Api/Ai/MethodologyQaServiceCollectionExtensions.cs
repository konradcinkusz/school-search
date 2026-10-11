using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Wiring for methodology Q&amp;A. Requires <see cref="AiServiceCollectionExtensions.AddAiServices"/> to run first: the kill
/// switch, the key check, the model setting and the daily token budget are shared with the explain feature.
/// Without a key no SDK client is built and the endpoint answers 503 (P8).
/// </summary>
public static class MethodologyQaServiceCollectionExtensions
{
    public static IServiceCollection AddMethodologyQa(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MethodologyQaOptions>(configuration.GetSection(MethodologyQaOptions.SectionName));

        var apiKey = Clean(configuration[AiServiceCollectionExtensions.ApiKeyVariable]);
        if (apiKey is not null)
        {
            AnthropicMethodologyQaClient.Register(services, apiKey);
        }
        else
        {
            services.TryAddSingleton<IMethodologyQaClient, UnconfiguredMethodologyQaClient>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.TryAddSingleton<TokenBudget>();
        services.TryAddSingleton(_ => MethodologyDocument.LoadEmbedded());
        services.AddSingleton<MethodologyQaRateLimiter>();
        services.AddScoped<MethodologyQaService>();

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
