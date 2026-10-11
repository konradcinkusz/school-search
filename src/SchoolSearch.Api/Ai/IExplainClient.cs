namespace SchoolSearch.Api.Ai;

/// <summary>
/// One model completion. <see cref="Text"/> is empty when the model refused or was cut off at the output cap;
/// the explain service treats empty text as rejected output, and the tokens are still counted.
/// </summary>
public sealed record ExplainCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam between the explain service and a model provider. Production uses
/// <c>AnthropicExplainClient</c> (the single file that references the SDK); tests inject a fake.
/// </summary>
public interface IExplainClient
{
    Task<ExplainCompletion> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>Registered when no key is configured. The explain service checks availability first, so this is never called.</summary>
internal sealed class UnconfiguredExplainClient : IExplainClient
{
    public Task<ExplainCompletion> CompleteAsync(string system, string user, CancellationToken ct) =>
        throw new InvalidOperationException("AI explain is not configured; the service must check availability before calling a model.");
}
