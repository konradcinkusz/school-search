using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Wiring for the AI explain feature. Called from Program.cs. Without a key the explain endpoint answers 503 and
/// nothing else changes (P8); no SDK client is constructed.
/// </summary>
public static class AiServiceCollectionExtensions
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string EnabledVariable = "AI_ENABLED";
    public const string ModelVariable = "AI_MODEL";
    public const string DailyBudgetVariable = "AI_DAILY_TOKEN_BUDGET";

    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.PostConfigure<AiOptions>(options =>
        {
            if (bool.TryParse(Clean(configuration[EnabledVariable]), out var enabled))
            {
                options.Enabled = enabled;
            }

            if (Clean(configuration[ModelVariable]) is { } model)
            {
                options.Model = model;
            }

            if (long.TryParse(Clean(configuration[DailyBudgetVariable]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var budget))
            {
                options.DailyTokenBudget = Math.Max(0, budget);
            }
        });

        var apiKey = Clean(configuration[ApiKeyVariable]);
        services.AddSingleton(sp =>
        {
            var enabled = sp.GetRequiredService<IOptions<AiOptions>>().Value.Enabled;
            if (!enabled)
            {
                return new AiAvailability(false, "AI_ENABLED is false; AI explain is switched off");
            }

            return apiKey is null
                ? new AiAvailability(false, "ANTHROPIC_API_KEY is not set; AI explain is disabled")
                : new AiAvailability(true, "AI explain is configured");
        });

        if (apiKey is not null)
        {
            AnthropicExplainClient.Register(services, apiKey);
        }
        else
        {
            services.TryAddSingleton<IExplainClient, UnconfiguredExplainClient>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<TokenBudget>();
        services.AddSingleton<ExplainRateLimiter>();
        services.AddScoped<ExplainService>();

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
