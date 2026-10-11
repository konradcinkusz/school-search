using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// Wiring for AI moderation triage. Called from Program.cs (the WIRING-AI line). Without a key, or with AI_ENABLED=false,
/// the worker stays idle, no SDK client is constructed and /health lists the integration "anthropic" as disabled (P8).
/// Requires the ReviewsDbContext and the IntegrationStatusRegistry (from ServiceDefaults) to be registered.
/// </summary>
public static class AiTriageServiceCollectionExtensions
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string EnabledVariable = "AI_ENABLED";
    public const string ModelVariable = "AI_MODEL";
    public const string DailyBudgetVariable = "AI_DAILY_TOKEN_BUDGET";

    public static IServiceCollection AddReviewTriage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiTriageOptions>(configuration.GetSection(AiTriageOptions.SectionName));
        services.PostConfigure<AiTriageOptions>(options =>
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
            var enabled = sp.GetRequiredService<IOptions<AiTriageOptions>>().Value.Enabled;
            if (!enabled)
            {
                return new AiTriageAvailability(false, "AI_ENABLED is false; AI review triage is switched off");
            }

            return apiKey is null
                ? new AiTriageAvailability(false, "ANTHROPIC_API_KEY is not set; AI review triage is disabled")
                : new AiTriageAvailability(true, "AI review triage is configured");
        });

        if (apiKey is not null)
        {
            AnthropicReviewTriageClient.Register(services, apiKey);
        }
        else
        {
            services.TryAddSingleton<IReviewTriageClient, UnconfiguredReviewTriageClient>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ReviewTriageTokenBudget>();
        services.AddSingleton<ReviewTriageRateLimiter>();
        services.AddScoped<ReviewTriageService>();
        services.AddHostedService<ReviewTriageWorker>();

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
