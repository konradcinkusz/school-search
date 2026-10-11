using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Import.Catchment;
using SchoolSearch.Import.Cke;
using SchoolSearch.Import.Geo;
using SchoolSearch.Import.Preschools;

namespace SchoolSearch.Import;

/// <summary>
/// One-shot importer. Exit codes: 0 ok, 1 error (bad arguments, configuration, loader failure),
/// 2 schema mismatch (the catalog lacks <see cref="CatalogSchema.LatestMigrationId"/>, nothing is written).
/// Rejected rows do not change the exit code: they are counted and listed in the output.
/// </summary>
internal static class Program
{
    internal const int ExitOk = 0;
    internal const int ExitError = 1;
    internal const int ExitSchemaMismatch = 2;

    private const int MaxReasonsPrinted = 50;
    private const string ProviderKey = "DATABASE_PROVIDER";
    private const string ConnectionKey = "ConnectionStrings__catalogdb";

    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            return await RunAsync(args, cts.Token);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Usage error: {ex.Message}");
            Console.Error.WriteLine(Usage);
            return ExitError;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Import cancelled. Its ImportRun row keeps FinishedAt empty.");
            return ExitError;
        }
        catch (Exception ex)
        {
            // Type and message only: provider messages must not echo the connection string, and no data rows are printed.
            Console.Error.WriteLine($"Import failed ({ex.GetType().Name}): {ex.Message}");
            return ExitError;
        }
    }

    internal static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? ExitError : ExitOk;
        }

        var command = args[0];
        var options = ParseOptions(args.AsSpan(1));

        await using var db = CreateContext();

        if (!db.Database.IsRelational())
        {
            // InMemory is for dry runs and tests only; it starts empty every process.
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (!await SchemaGuard.IsCompatibleAsync(db, ct))
        {
            Console.Error.WriteLine(
                $"Schema mismatch: __EFMigrationsHistory lacks '{CatalogSchema.LatestMigrationId}'. " +
                "Apply the Api migrations first (the Api owns the schema). Nothing was written.");
            return ExitSchemaMismatch;
        }

        switch (command)
        {
            case "check-schema":
                Console.WriteLine($"Schema ok: '{CatalogSchema.LatestMigrationId}' is applied.");
                return ExitOk;

            case "sio":
                {
                    var file = Required(options, "file");
                    var year = RequiredInt(options, "year");
                    var asOf = options.TryGetValue("as-of", out var asOfText)
                        ? DateOnly.ParseExact(asOfText, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : (DateOnly?)null;
                    var map = ColumnMapLoader.Load<SioColumnMap>(Optional(options, "map"));

                    return await RunLoaderAsync(db, "sio", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await SioLoader.RunAsync(context, stream, year, map, TimeProvider.System, token, asOf);
                    });
                }

            case "cke":
                {
                    var file = Required(options, "file");
                    var year = RequiredInt(options, "year");
                    var map = ColumnMapLoader.Load<CkeColumnMap>(Optional(options, "map"));

                    return await RunLoaderAsync(db, "cke", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await CkeLoader.RunAsync(context, stream, year, map, TimeProvider.System, token);
                    });
                }

            case "baseline":
                {
                    var file = Required(options, "file");
                    var year = RequiredInt(options, "year");

                    return await RunLoaderAsync(db, "baseline", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await BaselineLoader.RunAsync(context, stream, year, TimeProvider.System, token);
                    });
                }

            case "geo":
                {
                    var file = Required(options, "file");

                    return await RunLoaderAsync(db, "geo", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await GeoLoader.RunAsync(context, stream, TimeProvider.System, token);
                    });
                }

            case "catchment":
                {
                    var file = Required(options, "file");

                    return await RunLoaderAsync(db, "catchment", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await CatchmentLoader.RunAsync(context, stream, TimeProvider.System, token);
                    });
                }

            case "sio-preschools":
                {
                    var file = Required(options, "file");
                    var asOf = options.TryGetValue("as-of", out var asOfText)
                        ? DateOnly.ParseExact(asOfText, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : (DateOnly?)null;
                    var map = ColumnMapLoader.Load<PreschoolSioColumnMap>(Optional(options, "map"));

                    return await RunLoaderAsync(db, "sio-preschools", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await PreschoolSioLoader.RunAsync(context, stream, map, TimeProvider.System, token, asOf);
                    });
                }

            case "preschool-facts":
                {
                    var file = Required(options, "file");
                    var source = new PreschoolFactsSource(
                        Required(options, "source-name"),
                        Required(options, "source-url"),
                        DateOnly.ParseExact(Required(options, "as-of"), "yyyy-MM-dd", CultureInfo.InvariantCulture));

                    return await RunLoaderAsync(db, "preschool-facts", ct, async (context, token) =>
                    {
                        await using var stream = File.OpenRead(file);
                        return await PreschoolFactsLoader.RunAsync(context, stream, source, token);
                    });
                }

            default:
                throw new ArgumentException($"Unknown command '{command}'.");
        }
    }

    /// <summary>
    /// Records the run around a loader call. The loader's own writes and the ImportRun row go through the same context.
    /// </summary>
    private static async Task<int> RunLoaderAsync(
        CatalogDbContext db,
        string kind,
        CancellationToken ct,
        Func<CatalogDbContext, CancellationToken, Task<ImportReport>> loader)
    {
        var recorder = new ImportRunRecorder(db, TimeProvider.System);
        var run = await recorder.StartAsync(kind, ct);

        var report = await loader(db, ct);

        await recorder.CompleteAsync(run, report, ct);

        Console.WriteLine(
            $"{kind}: read={report.RowsRead} written={report.RowsWritten} rejected={report.RowsRejected} run={run.Id}");

        foreach (var reason in report.RejectionReasons.Take(MaxReasonsPrinted))
        {
            Console.WriteLine($"  rejected: {reason}");
        }

        if (report.RejectionReasons.Count > MaxReasonsPrinted)
        {
            Console.WriteLine($"  ... {report.RejectionReasons.Count - MaxReasonsPrinted} more rejection reasons not shown");
        }

        return ExitOk;
    }

    private static CatalogDbContext CreateContext()
    {
        var provider = (Environment.GetEnvironmentVariable(ProviderKey) ?? string.Empty).Trim().ToLowerInvariant();
        var builder = new DbContextOptionsBuilder<CatalogDbContext>();

        switch (provider)
        {
            case "postgres":
                {
                    var connection = Environment.GetEnvironmentVariable(ConnectionKey);
                    if (string.IsNullOrWhiteSpace(connection))
                    {
                        throw new InvalidOperationException($"{ProviderKey}=postgres needs {ConnectionKey}.");
                    }

                    builder.UseNpgsql(connection);
                    break;
                }

            case "inmemory":
                builder.UseInMemoryDatabase("catalog-import-dry-run");
                break;

            default:
                // Unset is an error here, not a silent in-memory store: a missing setting would otherwise lose the import.
                throw new InvalidOperationException(
                    $"{ProviderKey} must be 'postgres' or 'inmemory' (got '{provider}').");
        }

        return new CatalogDbContext(builder.Options);
    }

    private static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (!name.StartsWith("--", StringComparison.Ordinal) || name.Length <= 2)
            {
                throw new ArgumentException($"Expected an option like --file, found '{name}'.");
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{name}' needs a value.");
            }

            var key = name[2..];
            if (!options.TryAdd(key, args[i + 1]))
            {
                throw new ArgumentException($"Option '{name}' given twice.");
            }

            i++;
        }

        return options;
    }

    private static string Required(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value)
            ? value
            : throw new ArgumentException($"Missing required option --{key}.");

    private static int RequiredInt(Dictionary<string, string> options, string key) =>
        int.TryParse(Required(options, key), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"Option --{key} must be a whole number.");

    private static string? Optional(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out var value) ? value : null;

    private const string Usage = """
        Usage: SchoolSearch.Import <command> [options]
          check-schema
          sio       --file <xlsx> --year <yyyy> [--as-of yyyy-MM-dd] [--map <json>]
          cke       --file <csv>  --year <yyyy> [--map <json>]
          baseline  --file <csv>  --year <yyyy>
          geo       --file <csv>
          catchment --file <csv>
          sio-preschools   --file <csv> [--as-of yyyy-MM-dd] [--map <json>]
          preschool-facts  --file <csv> --source-name <text> --source-url <url> --as-of yyyy-MM-dd
        Environment: DATABASE_PROVIDER=postgres|inmemory, ConnectionStrings__catalogdb (postgres).
        Exit codes: 0 ok, 1 error, 2 schema mismatch.
        """;
}
