using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Billing.Errors;
using SchoolSearch.ServiceDefaults;

namespace SchoolSearch.Billing.Hosting;

/// <summary>
/// Production data-loss guard. The InMemory store loses every row on restart, so in Production, while the database
/// integration is degraded to in-memory, every non-read request answers 503 storage_unavailable. Reads (GET, HEAD) and
/// the probes keep working; /health reports the database as degraded. Outside Production nothing changes.
/// </summary>
public sealed class StorageGuardMiddleware(RequestDelegate next, IHostEnvironment environment, IntegrationStatusRegistry registry)
{
    public const string Code = "storage_unavailable";
    public const string Message = "Storage is not available right now. Try again later.";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsWriteRefused(context.Request.Method) || !IsVolatileStoreInProduction())
        {
            await next(context);
            return;
        }

        await ApiErrorWriter.WriteAsync(context, StatusCodes.Status503ServiceUnavailable, Code, Message, context.RequestAborted);
    }

    internal static bool IsWriteRefused(string method) =>
        !string.Equals(method, HttpMethods.Get, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, HttpMethods.Head, StringComparison.OrdinalIgnoreCase);

    private bool IsVolatileStoreInProduction() =>
        environment.IsProduction()
        && registry.Snapshot().Any(entry =>
            entry.Name == DatabaseProviderExtensions.IntegrationName && entry.State == IntegrationState.Degraded);
}
