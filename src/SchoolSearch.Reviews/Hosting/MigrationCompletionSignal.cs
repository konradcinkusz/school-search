namespace SchoolSearch.Reviews.Hosting;

/// <summary>
/// Completed by <see cref="MigrationHostedService"/> once schema work has finished (success or not).
/// Any background service that writes reviews (E6 triage worker) awaits <see cref="Completion"/> before touching the database.
/// </summary>
public sealed class MigrationCompletionSignal
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Result is true when the schema is ready, false when it could not be applied.</summary>
    public Task<bool> Completion => _completion.Task;

    public void Complete(bool succeeded) => _completion.TrySetResult(succeeded);
}
