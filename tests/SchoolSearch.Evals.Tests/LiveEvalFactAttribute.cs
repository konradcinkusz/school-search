using Xunit;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// A fact that calls the real model. Skipped unless the environment has <c>EVALS_LIVE=1</c> and a non-empty
/// <c>ANTHROPIC_API_KEY</c>, so CI (which has neither) never reaches the network. The key is read, never printed.
/// </summary>
public sealed class LiveEvalFactAttribute : FactAttribute
{
    public LiveEvalFactAttribute()
    {
        if (!EvalLive.IsEnabled)
        {
            Skip = "Live eval not run: needs EVALS_LIVE=1 and ANTHROPIC_API_KEY (real model calls). CI skips it by design.";
        }
    }
}

/// <summary>The live switch. <see cref="ApiKey"/> is for the SDK client only; it must never be logged or written to a report.</summary>
public static class EvalLive
{
    public const string EnableVariable = "EVALS_LIVE";
    public const string KeyVariable = "ANTHROPIC_API_KEY";

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable));

    public static string ApiKey =>
        Environment.GetEnvironmentVariable(KeyVariable) ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
}
