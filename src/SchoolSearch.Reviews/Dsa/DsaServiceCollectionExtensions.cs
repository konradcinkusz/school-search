using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.Reviews.Dsa;

/// <summary>Registers the DSA machinery. Called from the composition root (Program.cs).</summary>
public static class DsaServiceCollectionExtensions
{
    public static IServiceCollection AddDsaServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DsaOptions>().Bind(configuration.GetSection(DsaOptions.SectionName));
        services.AddSingleton(new DsaSubjectHasher(configuration["Reviews:SubjectPepper"]));
        services.AddScoped<DsaModerationService>();

        return services;
    }
}
