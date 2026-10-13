using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.Billing.DevSeed;

/// <summary>Wiring for the dev seed. The route is mapped by <see cref="DevSeedEndpoints"/> and answers 404 unless the gate allows it.</summary>
public static class DevSeedServiceCollectionExtensions
{
    public static IServiceCollection AddDevSeedServices(this IServiceCollection services)
    {
        services.AddScoped<DevSeeder>();
        return services;
    }
}
