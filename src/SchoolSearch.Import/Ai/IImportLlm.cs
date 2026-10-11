namespace SchoolSearch.Import.Ai;

/// <summary>
/// The one model seam for the offline extractors (catchment-extract, preschool-extract). Callers talk to this
/// interface only; <c>AnthropicImportLlm</c> is the single file in the Import project that references the SDK.
/// </summary>
/// <remarks>
/// <para>Contract: <paramref name="system"/> carries the operator rules, <paramref name="user"/> carries the task and the
/// untrusted document, wrapped in delimited blocks by the caller. Empty <see cref="ImportLlmReply.Text"/> means the
/// model refused or was cut off (max tokens); callers treat that as a failed chunk, never as an empty success.</para>
/// <para>Never reads or writes a database. Implementations never log the key or the document text.</para>
/// </remarks>
public interface IImportLlm
{
    Task<ImportLlmReply> CompleteAsync(string system, string user, int maxTokens, CancellationToken ct);
}

/// <summary>One model answer. <see cref="Text"/> is empty for a refusal or a max-tokens cut-off.</summary>
public record ImportLlmReply(string Text, int InputTokens, int OutputTokens);
