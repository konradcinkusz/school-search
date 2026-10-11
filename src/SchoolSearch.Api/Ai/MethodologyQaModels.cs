namespace SchoolSearch.Api.Ai;

/// <summary>Body of <c>POST /api/v1/ai/methodology-answer</c>.</summary>
public sealed record MethodologyAnswerRequest(string? Question);

/// <summary>
/// The response of <c>POST /api/v1/ai/methodology-answer</c>. <see cref="Answer"/> is plain text; <see cref="CitedSections"/>
/// are heading texts that exist in the methodology document; <see cref="MethodologyVersion"/> is the fact the answer relies on.
/// </summary>
public sealed record MethodologyAnswerResponse(
    string Answer,
    IReadOnlyList<string> CitedSections,
    string AiLabel,
    string Model,
    bool GroundedInDocument,
    string MethodologyVersion);

public enum MethodologyQaStatus
{
    Ok,
    InvalidQuestion,
    PersonalDataDetected,
    Unavailable,
    BudgetExhausted,
    RateLimited,
    OutputRejected,
}

/// <summary>Result of one call. <see cref="Response"/> is set only for <see cref="MethodologyQaStatus.Ok"/>.</summary>
public sealed record MethodologyQaOutcome(MethodologyQaStatus Status, MethodologyAnswerResponse? Response = null);

/// <summary>One model completion. <see cref="Text"/> is empty when the model refused or was cut off at the output cap.</summary>
public sealed record MethodologyQaCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// The only seam between the methodology service and a model provider. Production uses <c>AnthropicMethodologyQaClient</c>
/// (the single file of this feature that references the SDK); tests inject a fake.
/// </summary>
public interface IMethodologyQaClient
{
    Task<MethodologyQaCompletion> CompleteAsync(string system, string user, CancellationToken ct);
}

/// <summary>Registered when no key is configured. The service checks availability first, so this is never called.</summary>
internal sealed class UnconfiguredMethodologyQaClient : IMethodologyQaClient
{
    public Task<MethodologyQaCompletion> CompleteAsync(string system, string user, CancellationToken ct) =>
        throw new InvalidOperationException("Methodology Q&A is not configured; the service must check availability before calling a model.");
}
