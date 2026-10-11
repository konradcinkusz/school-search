namespace SchoolSearch.Api.Ai;

/// <summary>
/// One model completion for correction triage. <see cref="Text"/> is empty when the model refused or was cut off;
/// the service then rejects the output. Tokens are counted either way.
/// </summary>
public sealed record CorrectionTriageCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam between correction triage and a model provider. Production uses
/// <c>AnthropicCorrectionTriageClient</c> (the single file that references the SDK for this feature); tests inject a fake.
/// </summary>
public interface ICorrectionTriageClient
{
    Task<CorrectionTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>Registered when no key is configured. The service checks availability first, so this is never called.</summary>
internal sealed class UnconfiguredCorrectionTriageClient : ICorrectionTriageClient
{
    public Task<CorrectionTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct) =>
        throw new InvalidOperationException("Correction triage is not configured; the service must check availability before calling a model.");
}
