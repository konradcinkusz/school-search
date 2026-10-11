using System.Text.Json;

namespace SchoolSearch.Evals.Tests;

/// <summary>
/// One line of <c>evals/datasets/&lt;feature&gt;/cases.jsonl</c>. <see cref="Input"/> and <see cref="Expect"/> stay as JSON
/// so that each feature reads its own shape. <see cref="SyntheticPii"/> marks the adversarial cases that deliberately
/// contain obviously fake contact patterns (top-level <c>"synthetic_pii": true</c> in the file).
/// </summary>
public sealed record EvalCase(string Id, string Class, JsonElement Input, JsonElement Expect, string? Notes, bool SyntheticPii);
