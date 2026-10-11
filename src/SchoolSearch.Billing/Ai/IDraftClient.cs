namespace SchoolSearch.Billing.Ai;

/// <summary>
/// One model completion. <see cref="Text"/> is empty when the model refused or was cut off; the service then treats the
/// attempt as rejected, and the tokens are still counted.
/// </summary>
public sealed record DraftCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam between the draft service and a model provider. Production uses <c>AnthropicDraftClient</c> (the single
/// file in SchoolSearch.Billing that references the SDK); tests inject a fake.
/// </summary>
public interface IDraftClient
{
    Task<DraftCompletion> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>Registered when no key is configured. The draft service checks availability first, so this is never called.</summary>
internal sealed class UnconfiguredDraftClient : IDraftClient
{
    public Task<DraftCompletion> CompleteAsync(string system, string user, CancellationToken ct) =>
        throw new InvalidOperationException("AI profile drafts are not configured; the service must check availability before calling a model.");
}
