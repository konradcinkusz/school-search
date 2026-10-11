using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// The Anthropic-backed correction triage client. Request and response shapes are copied from
/// <c>AnthropicExplainClient</c>. Referenced only by <see cref="CorrectionTriageServiceCollectionExtensions"/>.
/// </summary>
internal sealed class AnthropicCorrectionTriageClient(AnthropicClient client, IOptions<AiOptions> options) : ICorrectionTriageClient
{
    /// <summary>Registers the SDK-backed client. Called only when a key is configured.</summary>
    internal static void Register(IServiceCollection services, string apiKey) =>
        services.AddSingleton<ICorrectionTriageClient>(sp => new AnthropicCorrectionTriageClient(
            new AnthropicClient { ApiKey = apiKey },
            sp.GetRequiredService<IOptions<AiOptions>>()));

    public async Task<CorrectionTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var settings = options.Value;
        var parameters = new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = settings.MaxOutputTokens,
            Messages = [new() { Role = Role.User, Content = system + "\n\n" + user }],
        };

        var message = await client.Messages.Create(parameters).WaitAsync(ct);

        var text = string.Concat(message.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text));

        // A refusal or a cut-off answer is not a usable triage: empty text makes the service answer ai_output_rejected.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new CorrectionTriageCompletion(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
