namespace SchoolSearch.Api.Ai;

/// <summary>
/// One model completion for search intent. <see cref="Text"/> is empty when the model refused or was cut off at the
/// output cap; the service treats empty text as rejected output, and the tokens are still counted.
/// </summary>
public sealed record SearchIntentCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam between the search-intent service and a model provider. Production uses
/// <c>AnthropicSearchIntentClient</c> (the single file that references the SDK); tests inject a fake.
/// </summary>
public interface ISearchIntentClient
{
    Task<SearchIntentCompletion> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>Registered when no key is configured. The service checks availability first, so this is never called.</summary>
internal sealed class UnconfiguredSearchIntentClient : ISearchIntentClient
{
    public Task<SearchIntentCompletion> CompleteAsync(string system, string user, CancellationToken ct) =>
        throw new InvalidOperationException("AI search intent is not configured; the service must check availability before calling a model.");
}
