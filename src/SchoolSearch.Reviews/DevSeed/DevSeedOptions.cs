using Microsoft.Extensions.Hosting;

namespace SchoolSearch.Reviews.DevSeed;

/// <summary>
/// Local-development seed switch (config DevSeed:Enabled and DevSeed:Key, env DevSeed__Enabled and DevSeed__Key).
/// The seed route exists only when <see cref="IsEnabled"/> holds at startup. The key is a secret: never log it.
/// </summary>
public sealed class DevSeedOptions
{
    public const string SectionName = "DevSeed";

    /// <summary>Must be true, together with a non-empty key, outside Production.</summary>
    public bool Enabled { get; set; }

    /// <summary>Shared secret sent in the X-Dev-Seed-Key header.</summary>
    public string Key { get; set; } = "";

    public static bool IsEnabled(DevSeedOptions options, IHostEnvironment environment) =>
        options.Enabled
        && !string.IsNullOrWhiteSpace(options.Key)
        && !environment.IsProduction();
}
