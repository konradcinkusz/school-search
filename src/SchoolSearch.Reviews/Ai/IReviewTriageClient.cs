namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// One model completion. <see cref="Text"/> is empty when the model refused or was cut off; the parser then labels
/// the review unclear, and the tokens are still counted.
/// </summary>
public sealed record ReviewTriageCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam between the triage service and a model provider. Production uses <c>AnthropicReviewTriageClient</c>
/// (the single file that references the SDK); tests inject a fake.
/// </summary>
public interface IReviewTriageClient
{
    Task<ReviewTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>Registered when no key is configured. The triage service checks availability first, so this is never called.</summary>
internal sealed class UnconfiguredReviewTriageClient : IReviewTriageClient
{
    public Task<ReviewTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct) =>
        throw new InvalidOperationException("AI review triage is not configured; the service must check availability before calling a model.");
}
