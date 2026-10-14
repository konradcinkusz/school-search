namespace SchoolSearch.Api.Map;

/// <summary>Wiring for the map markers read. Called from the composition root (Program.cs).</summary>
public static class MapServiceCollectionExtensions
{
    public static IServiceCollection AddMapServices(this IServiceCollection services)
    {
        services.AddScoped<MapMarkerService>();
        return services;
    }
}
