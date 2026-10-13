using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.Reviews.DevSeed;

/// <summary>Wiring for the dev seed. Called from Program.cs. Reviews, DSA and the subject hasher must be registered too.</summary>
public static class DevSeedServiceCollectionExtensions
{
    public static IServiceCollection AddDevSeed(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DevSeedOptions>(configuration.GetSection(DevSeedOptions.SectionName));
        services.AddScoped<DevSeeder>();

        return services;
    }
}
