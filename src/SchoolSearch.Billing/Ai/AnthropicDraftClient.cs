using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Billing.Ai;

/// <summary>
/// The only file in SchoolSearch.Billing that references the Anthropic SDK. The calls copy
/// SchoolSearch.Api/Ai/AnthropicExplainClient.cs (client construction, request shape, response parsing).
/// </summary>
/// <remarks>
/// The system rules are sent as the first part of the user message, as in the Api. Sampling parameters are not set.
/// </remarks>
internal sealed class AnthropicDraftClient(AnthropicClient client, IOptions<AiDraftOptions> options) : IDraftClient
{
    /// <summary>Registers the SDK-backed client. Called only when a key is configured.</summary>
    internal static void Register(IServiceCollection services, string apiKey) =>
        services.AddSingleton<IDraftClient>(sp => new AnthropicDraftClient(
            new AnthropicClient { ApiKey = apiKey },
            sp.GetRequiredService<IOptions<AiDraftOptions>>()));

    public async Task<DraftCompletion> CompleteAsync(string system, string user, CancellationToken ct)
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

        // A refusal or a cut-off answer is not a usable draft: empty text makes the service try again, then answer draft_rejected.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new DraftCompletion(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
