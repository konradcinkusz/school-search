namespace SchoolSearch.Import.Ai;

/// <summary>
/// Token cap for one command run (input plus output, all chunks). A run that would exceed the cap stops before the
/// next call; it never overshoots silently. Cost guard only; it is not billing and is not persisted.
/// </summary>
public sealed class ImportTokenBudget(long limit)
{
    private long _spent;

    public long Limit { get; } = Math.Max(0, limit);

    public long Spent => Interlocked.Read(ref _spent);

    public long Remaining => Math.Max(0, Limit - Spent);

    public void Record(long tokens)
    {
        if (tokens > 0)
        {
            Interlocked.Add(ref _spent, tokens);
        }
    }
}

/// <summary>Raised before a call that the remaining budget cannot cover. The run stops; nothing is written.</summary>
public sealed class ImportBudgetExhaustedException(long limit, long spent)
    : InvalidOperationException(
        $"Token budget exhausted: spent {spent} of {limit} tokens. The run stopped before the next call; nothing was written. " +
        "Raise AI_IMPORT_TOKEN_BUDGET for a deliberate larger run.");

/// <summary>
/// Wraps an <see cref="IImportLlm"/> with the run budget. A call is refused when its worst case (input estimate plus
/// max output) does not fit the remaining budget, so the cap is respected even for the last call.
/// </summary>
public sealed class BudgetedImportLlm(IImportLlm inner, ImportTokenBudget budget) : IImportLlm
{
    // Conservative estimate: roughly two characters per token for Polish text, so the input is never under-counted.
    private const int CharsPerToken = 2;

    public async Task<ImportLlmReply> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(budget);

        var estimate = (system.Length + user.Length) / CharsPerToken + maxTokens;
        if (budget.Spent + estimate > budget.Limit)
        {
            throw new ImportBudgetExhaustedException(budget.Limit, budget.Spent);
        }

        var reply = await inner.CompleteAsync(system, user, maxTokens, ct);
        budget.Record((long)reply.InputTokens + reply.OutputTokens);
        return reply;
    }
}
