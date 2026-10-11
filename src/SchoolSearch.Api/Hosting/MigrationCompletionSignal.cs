namespace SchoolSearch.Api.Hosting;

/// <summary>
/// Completed by <see cref="MigrationHostedService"/> once schema work has finished (success or not).
/// Any other background service awaits <see cref="Completion"/> before touching the database (SERVICE-API-PATTERNS §7).
/// </summary>
public sealed class MigrationCompletionSignal
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Result is true when the schema is ready, false when it could not be applied.</summary>
    public Task<bool> Completion => _completion.Task;

    public void Complete(bool succeeded) => _completion.TrySetResult(succeeded);
}
