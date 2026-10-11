using System.Text.Json;
using SchoolSearch.Import.Ai;

namespace SchoolSearch.Import.Tests;

/// <summary>
/// Scripted model for the preschool extractor. Replies come out in order; every system and user turn is recorded so a
/// test can check what would have been sent. No network.
/// </summary>
internal sealed class ScriptedPreschoolLlm : IImportLlm
{
    private readonly Queue<ImportLlmReply> _replies;

    public ScriptedPreschoolLlm(params ImportLlmReply[] replies)
    {
        _replies = new Queue<ImportLlmReply>(replies);
    }

    public List<string> SystemTurns { get; } = [];

    public List<string> UserTurns { get; } = [];

    public Task<ImportLlmReply> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        SystemTurns.Add(system);
        UserTurns.Add(user);
        var reply = _replies.Count > 0 ? _replies.Dequeue() : new ImportLlmReply(string.Empty, 0, 0);
        return Task.FromResult(reply);
    }

    /// <summary>A well-formed reply holding the given rows.</summary>
    public static ImportLlmReply Rows(params object[] rows) =>
        new(JsonSerializer.Serialize(new { rows }), 120, 60);

    /// <summary>A reply that the model gave as prose, which must fail the chunk.</summary>
    public static ImportLlmReply Prose(string text) => new(text, 120, 60);

    /// <summary>A refusal or cut-off: empty text.</summary>
    public static ImportLlmReply Empty() => new(string.Empty, 120, 0);
}
