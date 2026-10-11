using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolSearch.ServiceDefaults;

/// <summary>The state of one optional integration (P8). Serialized as ok | degraded | disabled.</summary>
public enum IntegrationState
{
    /// <summary>Configured and working.</summary>
    Ok,

    /// <summary>Running with a fallback or reduced function (for example, in-memory storage).</summary>
    Degraded,

    /// <summary>Intentionally absent.</summary>
    Disabled
}

/// <summary>One entry in the integration list shown by <c>/health</c> and the startup banner.</summary>
public sealed record IntegrationStatus(string Name, IntegrationState State, string? Detail);

/// <summary>
/// Singleton registry of optional integrations. Services call <see cref="Register"/> while they
/// are configured; <c>/health</c> and the startup banner read <see cref="Snapshot"/>.
/// </summary>
public sealed class IntegrationStatusRegistry
{
    private readonly ConcurrentDictionary<string, IntegrationStatus> _entries = new(StringComparer.Ordinal);

    public void Register(string name, IntegrationState state, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _entries[name] = new IntegrationStatus(name, state, detail);
    }

    public IReadOnlyList<IntegrationStatus> Snapshot() =>
        _entries.Values.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
}

internal static class IntegrationStatusRegistration
{
    /// <summary>
    /// Returns the single registry instance for this service collection, adding it if needed.
    /// Both <c>AddServiceDefaults</c> and <c>AddDatabaseContext</c> go through here, so the
    /// registry is one singleton regardless of call order.
    /// </summary>
    internal static IntegrationStatusRegistry GetOrAddRegistry(this IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(IntegrationStatusRegistry)
                && descriptor.ImplementationInstance is IntegrationStatusRegistry existing)
            {
                return existing;
            }
        }

        var registry = new IntegrationStatusRegistry();
        services.AddSingleton(registry);
        return registry;
    }
}
