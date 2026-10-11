using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Reviews.Ai;

/// <summary>
/// The only file in SchoolSearch.Reviews that references the Anthropic SDK. The calls copy
/// SchoolSearch.Api/Ai/AnthropicExplainClient.cs (client construction, request shape, response parsing).
/// </summary>
/// <remarks>
/// The system rules are sent as the first part of the user message, as in the Api. Sampling parameters are not set.
/// </remarks>
internal sealed class AnthropicReviewTriageClient(AnthropicClient client, IOptions<AiTriageOptions> options) : IReviewTriageClient
{
    /// <summary>Registers the SDK-backed client. Called only when a key is configured.</summary>
    internal static void Register(IServiceCollection services, string apiKey) =>
        services.AddSingleton<IReviewTriageClient>(sp => new AnthropicReviewTriageClient(
            new AnthropicClient { ApiKey = apiKey },
            sp.GetRequiredService<IOptions<AiTriageOptions>>()));

    public async Task<ReviewTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct)
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

        // A refusal or a cut-off answer is not a usable triage: empty text makes the parser label the review unclear.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new ReviewTriageCompletion(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
