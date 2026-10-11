using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.ServiceDefaults;

/// <summary>
/// Provider selection (P4, P8). <c>DATABASE_PROVIDER</c> is <c>postgres</c> or <c>sqlserver</c>;
/// unset or <c>inmemory</c> selects the InMemory store. A persistent provider with no connection
/// string degrades to InMemory rather than failing startup. Any other value fails fast: it is a typo, not an optional integration.
/// Schema is applied by the owning service (MigrateAsync on real providers, EnsureCreated only on InMemory).
/// </summary>
public static class DatabaseProviderExtensions
{
    public const string ProviderKey = "DATABASE_PROVIDER";
    public const string IntegrationName = "database";

    private const int RetryMaxCount = 10;
    private const int CommandTimeoutSeconds = 60;
    private static readonly TimeSpan RetryMaxDelay = TimeSpan.FromSeconds(30);

    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Source of <c>DATABASE_PROVIDER</c> and the connection string.</param>
    /// <param name="connectionName">Connection string name, for example <c>catalogdb</c>.</param>
    /// <param name="inMemoryName">InMemory database name, used when no persistent store is configured.</param>
    public static IServiceCollection AddDatabaseContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionName,
        string inMemoryName)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(inMemoryName);

        var registry = services.GetOrAddRegistry();
        var provider = (configuration[ProviderKey] ?? string.Empty).Trim().ToLowerInvariant();

        if (provider == "postgres"
            && configuration.GetConnectionString(connectionName) is { Length: > 0 } postgresConnection)
        {
            services.AddDbContext<TContext>(options => options.UseNpgsql(
                postgresConnection,
                npgsql => npgsql.EnableRetryOnFailure(RetryMaxCount, RetryMaxDelay, null)
                    .CommandTimeout(CommandTimeoutSeconds)));
            registry.Register(IntegrationName, IntegrationState.Ok, "postgres");
            return services;
        }

        if (provider == "sqlserver"
            && configuration.GetConnectionString(connectionName) is { Length: > 0 } sqlServerConnection)
        {
            services.AddDbContext<TContext>(options => options.UseSqlServer(
                sqlServerConnection,
                sql => sql.EnableRetryOnFailure(RetryMaxCount, RetryMaxDelay, null)
                    .CommandTimeout(CommandTimeoutSeconds)));
            registry.Register(IntegrationName, IntegrationState.Ok, "sqlserver");
            return services;
        }

        switch (provider)
        {
            case "postgres" or "sqlserver":
                registry.Register(
                    IntegrationName,
                    IntegrationState.Degraded,
                    $"{provider} selected but no connection string '{connectionName}'; using in-memory store (no persistence)");
                break;

            case "" or "inmemory":
                registry.Register(IntegrationName, IntegrationState.Degraded, "in-memory store (no persistence)");
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported {ProviderKey} value '{provider}'. Expected 'postgres', 'sqlserver', 'inmemory' or unset.");
        }

        services.AddDbContext<TContext>(options => options.UseInMemoryDatabase(inMemoryName));
        return services;
    }
}
