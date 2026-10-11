using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// The only file of the methodology feature that references the Anthropic SDK. Request and response shapes are copied
/// from <c>AnthropicExplainClient</c>, so an SDK change is a one-file fix. Name matches the <c>Anthropic*Client</c> rule of the AI boundary tests.
/// </summary>
/// <remarks>The system rules and the document are sent as the first part of the user message, as in the explain client.</remarks>
internal sealed class AnthropicMethodologyQaClient(AnthropicClient client, IOptions<AiOptions> options) : IMethodologyQaClient
{
    /// <summary>Registers the SDK-backed client. Called only when a key is configured.</summary>
    internal static void Register(IServiceCollection services, string apiKey) =>
        services.AddSingleton<IMethodologyQaClient>(sp => new AnthropicMethodologyQaClient(
            new AnthropicClient { ApiKey = apiKey },
            sp.GetRequiredService<IOptions<AiOptions>>()));

    public async Task<MethodologyQaCompletion> CompleteAsync(string system, string user, CancellationToken ct)
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

        // A refusal or a cut-off answer is not a usable answer: empty text makes the service answer ai_output_rejected.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new MethodologyQaCompletion(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
