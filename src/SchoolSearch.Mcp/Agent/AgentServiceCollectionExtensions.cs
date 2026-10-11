using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SchoolSearch.Mcp.Options;
using SchoolSearch.Mcp.Reviews;
using SchoolSearch.Mcp.Tools;

namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// Wiring for the agent. Called from Program.cs; <see cref="MapAgentEndpoints"/> maps the route. Requires
/// AddCatalogClient and AddReviewsClient to be registered (the agent's tools use those clients). Without
/// ANTHROPIC_API_KEY, or with AI_ENABLED=false, no model client is registered and the endpoint answers 503.
/// </summary>
public static class AgentServiceCollectionExtensions
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string EnabledVariable = "AI_ENABLED";
    public const string ModelVariable = "AI_MODEL";
    public const string DailyBudgetVariable = "AI_DAILY_TOKEN_BUDGET";
    public const string RequestsPerMinuteVariable = "AGENT_REQUESTS_PER_IP_PER_MINUTE";

    public static IServiceCollection AddAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var apiKey = Clean(configuration[ApiKeyVariable]);
        var enabled = !bool.TryParse(Clean(configuration[EnabledVariable]), out var flag) || flag;

        var options = new AgentOptions { Enabled = apiKey is not null && enabled };
        if (Clean(configuration[ModelVariable]) is { } model)
        {
            options.Model = model;
        }

        if (long.TryParse(Clean(configuration[DailyBudgetVariable]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var budget))
        {
            options.DailyTokenBudget = Math.Max(0, budget);
        }

        if (int.TryParse(Clean(configuration[RequestsPerMinuteVariable]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var perMinute))
        {
            options.RequestsPerIpPerMinute = Math.Max(1, perMinute);
        }

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));
        services.AddSingleton<AgentTokenBudget>();
        services.AddSingleton<AgentRateLimiter>();

        services.AddTransient<SchoolTools>();
        services.AddTransient<PreschoolTools>();
        services.AddTransient<ReviewsTools>();
        services.AddTransient<AgentToolRunner>();

        if (options.Enabled)
        {
            // TryAdd: a test host may register its own IAgentLlm before this call.
            services.TryAddSingleton<IAgentLlm>(_ => AnthropicAgentLlm.Create(apiKey!, options));
            services.AddTransient<AgentService>();
        }

        return services;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
