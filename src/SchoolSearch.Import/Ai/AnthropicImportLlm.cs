using Anthropic;
using Anthropic.Models.Messages;

namespace SchoolSearch.Import.Ai;

/// <summary>
/// The only file in the Import project that references the Anthropic SDK. Request and response shapes are copied from
/// src/SchoolSearch.Api/Ai/AnthropicExplainClient.cs, so an SDK change is a one-file fix here.
/// </summary>
/// <remarks>
/// Runs on the operator's machine only, with the operator's key from ANTHROPIC_API_KEY (see <see cref="ImportLlmSetup"/>).
/// The key is never written to a file, logged or printed. The system rules are sent ahead of the task text in the user
/// message, as the Api client does, so the rules are delivered verbatim and before the document.
/// </remarks>
public sealed class AnthropicImportLlm : IImportLlm
{
    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicImportLlm(string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _client = new AnthropicClient { ApiKey = apiKey };
        _model = model;
    }

    public async Task<ImportLlmReply> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = maxTokens,
            Messages = [new() { Role = Role.User, Content = system + "\n\n" + user }],
        };

        var message = await _client.Messages.Create(parameters).WaitAsync(ct);

        var text = string.Concat(message.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text));

        // A refusal or a cut-off answer is not a usable result: empty text makes the caller fail the chunk.
        var stopReason = message.StopReason?.ToString();
        if (stopReason is "refusal" or "max_tokens")
        {
            text = string.Empty;
        }

        return new ImportLlmReply(
            text,
            (int)message.Usage.InputTokens,
            (int)message.Usage.OutputTokens);
    }
}
