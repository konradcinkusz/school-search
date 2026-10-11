using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Billing.Ai;

/// <summary>
/// Wiring for the AI profile-copy assistant. Called from Program.cs (the WIRING-AI line). Without a key, or with
/// AI_ENABLED=false, no SDK client is constructed, the endpoint answers 503 ai_unavailable and /health lists the
/// integration "anthropic" as disabled (P8). Needs the IntegrationStatusRegistry (from ServiceDefaults) and BillingDbContext.
/// </summary>
public static class AiDraftServiceCollectionExtensions
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string EnabledVariable = "AI_ENABLED";
    public const string ModelVariable = "AI_MODEL";
    public const string DailyBudgetVariable = "AI_DAILY_TOKEN_BUDGET";

    public static IServiceCollection AddProfileDraftAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiDraftOptions>(configuration.GetSection(AiDraftOptions.SectionName));
        services.PostConfigure<AiDraftOptions>(options =>
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
            var enabled = sp.GetRequiredService<IOptions<AiDraftOptions>>().Value.Enabled;
            if (!enabled)
            {
                return new DraftAvailability(false, "AI_ENABLED is false; AI profile drafts are switched off");
            }

            return apiKey is null
                ? new DraftAvailability(false, "ANTHROPIC_API_KEY is not set; AI profile drafts are disabled")
                : new DraftAvailability(true, "AI profile drafts are configured");
        });

        if (apiKey is not null)
        {
            AnthropicDraftClient.Register(services, apiKey);
        }
        else
        {
            services.TryAddSingleton<IDraftClient, UnconfiguredDraftClient>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DraftTokenBudget>();
        services.AddSingleton<DraftRateLimiter>();
        services.AddScoped<ProfileDraftService>();

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
