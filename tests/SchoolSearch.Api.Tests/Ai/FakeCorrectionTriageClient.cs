using SchoolSearch.Api.Ai;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Stands in for the model in correction triage tests. Records every call so tests can prove whether the model path
/// was taken. The default answer is a valid, digit-free draft, so it is grounded for every correction.
/// </summary>
public sealed class FakeCorrectionTriageClient : ICorrectionTriageClient
{
    public const string ValidAnswer =
        "{\"category\":\"typo\",\"fieldLooksValid\":true,\"severity\":\"low\",\"suggestedAction\":\"update_from_source\"," +
        "\"draftReplyPl\":\"Dziękujemy za zgłoszenie. Sprawdzimy podane źródło i zaktualizujemy dane, jeśli je potwierdza.\"}";

    private readonly object _gate = new();
    private readonly List<(string System, string User)> _prompts = new();
    private int _calls;

    public Func<string, string, CorrectionTriageCompletion> Respond { get; set; } =
        (_, _) => new CorrectionTriageCompletion(ValidAnswer, 120, 60);

    public int CallCount => Volatile.Read(ref _calls);

    public IReadOnlyList<(string System, string User)> Prompts
    {
        get
        {
            lock (_gate)
            {
                return _prompts.ToArray();
            }
        }
    }

    public Task<CorrectionTriageCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (_gate)
        {
            _prompts.Add((system, user));
        }

        return Task.FromResult(Respond(system, user));
    }
}
