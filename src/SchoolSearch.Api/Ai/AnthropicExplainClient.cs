using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// The only file in the solution that references the Anthropic SDK. Everything the SDK needs (client construction,
/// request shape, response parsing) stays here, so an SDK change is a one-file fix.
/// </summary>
/// <remarks>
/// The system rules are sent as the first part of the user message. This avoids depending on the shape of the SDK's
/// system parameter; the rules are still delivered verbatim and ahead of the facts.
/// Sampling parameters are not set; the model default applies.
/// </remarks>
internal sealed class AnthropicExplainClient(AnthropicClient client, IOptions<AiOptions> options) : IExplainClient
{
    /// <summary>Registers the SDK-backed client. Called only when a key is configured.</summary>
    internal static void Register(IServiceCollection services, string apiKey) =>
        services.AddSingleton<IExplainClient>(sp => new AnthropicExplainClient(
            new AnthropicClient { ApiKey = apiKey },
            sp.GetRequiredService<IOptions<AiOptions>>()));

    public async Task<ExplainCompletion> CompleteAsync(string system, string user, CancellationToken ct)
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

        // A refusal or a cut-off answer is not a usable explanation: empty text makes the service answer ai_output_rejected.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new ExplainCompletion(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
