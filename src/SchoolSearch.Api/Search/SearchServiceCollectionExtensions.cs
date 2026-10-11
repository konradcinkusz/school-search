using SchoolSearch.Api.Services;

namespace SchoolSearch.Api.Search;

/// <summary>Wiring for the public search surface. Called from the composition root (Program.cs / Extensions).</summary>
public static class SearchServiceCollectionExtensions
{
    public static IServiceCollection AddSearchServices(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<SchoolScoreService>();
        services.AddScoped<SearchReadService>();
        return services;
    }
}
