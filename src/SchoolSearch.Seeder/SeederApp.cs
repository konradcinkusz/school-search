using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolSearch.Seeder;

/// <summary>
/// Wires configuration, the HTTP client and the runner, and maps the outcome to an exit code:
/// 0 seeded, 1 a seeding step failed (or was cancelled), 2 configuration missing or invalid.
/// </summary>
public static class SeederApp
{
    public const int ExitOk = 0;
    public const int ExitSeedFailed = 1;
    public const int ExitConfiguration = 2;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <param name="environment">Variable lookup. Tests pass a dictionary.</param>
    /// <param name="handler">Optional HTTP handler. Tests pass a fake; null uses the real network stack.</param>
    public static async Task<int> RunAsync(
        Func<string, string?> environment,
        TextWriter output,
        TextWriter error,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        SeederOptions options;
        try
        {
            options = SeederOptions.FromEnvironment(environment);
        }
        catch (SeederConfigurationException ex)
        {
            error.WriteLine("Configuration error:");
            error.WriteLine(ex.Message);
            return ExitConfiguration;
        }

        using HttpClient client = handler is null
            ? new HttpClient()
            : new HttpClient(handler, disposeHandler: true);
        client.Timeout = RequestTimeout;

        var stopwatch = Stopwatch.StartNew();
        var runner = new SeedRunner(
            new HttpSeedTransport(client),
            output,
            (delay, token) => Task.Delay(delay, token),
            () => stopwatch.Elapsed);

        try
        {
            await runner.RunAsync(options, cancellationToken);
            return ExitOk;
        }
        catch (SeedFailureException ex)
        {
            error.WriteLine("Seeding failed: " + ex.Message);
            return ExitSeedFailed;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Seeding was cancelled.");
            return ExitSeedFailed;
        }
        catch (Exception ex)
        {
            // Type and message only: no stack trace, and no request or response content.
            error.WriteLine($"Seeding failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
            return ExitSeedFailed;
        }
    }
}
