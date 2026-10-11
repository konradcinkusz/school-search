using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SchoolSearch.Api.Demo;

/// <summary>Wiring for the demo seed. Called from Program.cs; the seed is off unless SCHOOLSEARCH_DEMO_SEED=true.</summary>
public static class DemoServiceCollectionExtensions
{
    public static IServiceCollection AddDemoSeed(this IServiceCollection services, IConfiguration configuration) =>
        services.AddIfEnabled(configuration);
}

/// <summary>
/// Self-registration of the demo seed. Registers <see cref="DemoSeedHostedService"/> only when
/// SCHOOLSEARCH_DEMO_SEED parses to true. Calling it twice registers the service once.
/// </summary>
public static class DemoSeedRegistration
{
    public const string FlagName = "SCHOOLSEARCH_DEMO_SEED";

    public static IServiceCollection AddIfEnabled(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = bool.TryParse(configuration[FlagName], out var value) && value;
        if (!enabled)
        {
            return services;
        }

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DemoSeedHostedService>());
        return services;
    }
}
