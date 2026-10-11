using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// The only file in Api/Ai/SearchIntent* that references the Anthropic SDK. The request shape is the one proven in
/// AnthropicExplainClient; the answer is plain JSON text, parsed defensively by SearchIntentParser.
/// </summary>
internal sealed class AnthropicSearchIntentClient(
    AnthropicClient client,
    IOptions<AiOptions> aiOptions,
    IOptions<SearchIntentOptions> intentOptions) : ISearchIntentClient
{
    /// <summary>Registers the SDK-backed client. Called only when a key is configured.</summary>
    internal static void Register(IServiceCollection services, string apiKey) =>
        services.AddSingleton<ISearchIntentClient>(sp => new AnthropicSearchIntentClient(
            new AnthropicClient { ApiKey = apiKey },
            sp.GetRequiredService<IOptions<AiOptions>>(),
            sp.GetRequiredService<IOptions<SearchIntentOptions>>()));

    public async Task<SearchIntentCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = aiOptions.Value.Model,
            MaxTokens = intentOptions.Value.MaxOutputTokens,
            Messages = [new() { Role = Role.User, Content = system + "\n\n" + user }],
        };

        var message = await client.Messages.Create(parameters).WaitAsync(ct);

        var text = string.Concat(message.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text));

        // A refusal or a cut-off answer is not a usable interpretation: empty text makes the service answer ai_output_rejected.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new SearchIntentCompletion(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
