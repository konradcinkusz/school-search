namespace SchoolSearch.Import.Ai;

/// <summary>
/// Stand-in used when no key is configured or the kill switch is off. Any call fails loudly; the commands check
/// <see cref="ImportLlmSetup.TryCreate"/> first, so operators see a clear message instead of this exception (P8).
/// </summary>
public sealed class UnconfiguredImportLlm(string reason) : IImportLlm
{
    public string Reason { get; } = reason;

    public Task<ImportLlmReply> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct) =>
        throw new InvalidOperationException(Reason);
}

/// <summary>
/// Builds the operator-side model client from the environment. Variables: <c>ANTHROPIC_API_KEY</c> (required, read here
/// only, never stored), <c>AI_ENABLED</c> (kill switch, <c>false</c> disables), <c>AI_MODEL</c> (pinned model id, default
/// <c>claude-haiku-5-5</c>), <c>AI_IMPORT_TOKEN_BUDGET</c> (per-run token cap, default 300000).
/// </summary>
public static class ImportLlmSetup
{
    public const string ApiKeyVariable = "ANTHROPIC_API_KEY";
    public const string EnabledVariable = "AI_ENABLED";
    public const string ModelVariable = "AI_MODEL";
    public const string BudgetVariable = "AI_IMPORT_TOKEN_BUDGET";

    public const string DefaultModel = "claude-haiku-5-5";
    public const long DefaultBudget = 300_000;

    /// <summary>
    /// Returns the budgeted client when the key is set and the kill switch is on. Otherwise returns false with an
    /// operator-facing reason that never contains the key.
    /// </summary>
    public static bool TryCreate(out IImportLlm llm, out ImportTokenBudget? budget, out string reason)
    {
        budget = null;

        var enabled = Environment.GetEnvironmentVariable(EnabledVariable)?.Trim();
        if (string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
        {
            llm = new UnconfiguredImportLlm("AI_ENABLED is false; the AI extractor is switched off. Nothing was written.");
            reason = ((UnconfiguredImportLlm)llm).Reason;
            return false;
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            llm = new UnconfiguredImportLlm($"{ApiKeyVariable} is not set; the AI extractor needs it on this machine. Nothing was written.");
            reason = ((UnconfiguredImportLlm)llm).Reason;
            return false;
        }

        var model = Environment.GetEnvironmentVariable(ModelVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        var limit = DefaultBudget;
        var budgetText = Environment.GetEnvironmentVariable(BudgetVariable)?.Trim();
        if (!string.IsNullOrWhiteSpace(budgetText)
            && (!long.TryParse(budgetText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out limit) || limit < 0))
        {
            llm = new UnconfiguredImportLlm($"{BudgetVariable} must be a whole number of tokens (0 or more).");
            reason = ((UnconfiguredImportLlm)llm).Reason;
            return false;
        }

        budget = new ImportTokenBudget(limit);
        llm = new BudgetedImportLlm(new AnthropicImportLlm(apiKey, model), budget);
        reason = $"AI extractor ready (model {model}, budget {limit} tokens for this run).";
        return true;
    }
}
