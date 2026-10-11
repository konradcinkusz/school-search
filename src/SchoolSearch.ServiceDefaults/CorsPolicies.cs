using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.ServiceDefaults;

/// <summary>Names of the CORS policies the kernel offers. Add a name here, never a string literal at the call site.</summary>
public static class CorsPolicies
{
    public const string Frontend = "frontend";
}

public static class CorsExtensions
{
    /// <summary>
    /// Registers one named CORS policy. Allowed origins come from
    /// <c>SchoolSearch:Cors:AllowedOrigins</c> (env: <c>SchoolSearch__Cors__AllowedOrigins__0</c>).
    /// With no origins configured the policy allows no cross-origin caller (fail closed).
    /// The read-only v1 surface allows GET, HEAD and OPTIONS only. Pair with UseCors in the service.
    /// </summary>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration, string policyName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);

        var origins = configuration.GetSection("SchoolSearch:Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(policyName, policy =>
        {
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins);
            }

            policy.WithMethods("GET", "HEAD", "OPTIONS").AllowAnyHeader();
        }));

        return services;
    }
}
