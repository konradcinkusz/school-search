using SchoolSearch.Api.Ai;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Stands in for the model in methodology tests. Records every call so tests can prove whether the SDK path was taken.
/// The default answer is grounded (no digits) and cites a heading that exists in the embedded document.
/// </summary>
public sealed class FakeMethodologyQaClient : IMethodologyQaClient
{
    public const string CoveredJson =
        "{\"answer\":\"Pokrycie to udział zdających w liczbie uczniów klasy VIII.\",\"covered\":true,\"citedSections\":[\"4.2 Coverage and the blend\"]}";

    private readonly object _gate = new();
    private readonly List<(string System, string User)> _prompts = new();
    private int _calls;

    public Func<string, string, MethodologyQaCompletion> Respond { get; set; } =
        (_, _) => new MethodologyQaCompletion(CoveredJson, 100, 50);

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

    public Task<MethodologyQaCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (_gate)
        {
            _prompts.Add((system, user));
        }

        return Task.FromResult(Respond(system, user));
    }
}
