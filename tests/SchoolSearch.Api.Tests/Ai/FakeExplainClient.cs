using SchoolSearch.Api.Ai;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Stands in for the model. Records every call so tests can prove whether the SDK path was taken, and answers with
/// <see cref="Respond"/>. The default answer contains no digits, so it is grounded for every school.
/// </summary>
public sealed class FakeExplainClient : IExplainClient
{
    public const string NeutralText = "Wynik opisuje szkołę względem średniej krajowej. Wynik nie jest oficjalnym rankingiem i nie ocenia nauczycieli ani uczniów.";

    private readonly object _gate = new();
    private readonly List<(string System, string User)> _prompts = new();
    private int _calls;

    public Func<string, string, ExplainCompletion> Respond { get; set; } =
        (_, _) => new ExplainCompletion(NeutralText, 100, 50);

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

    public Task<ExplainCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (_gate)
        {
            _prompts.Add((system, user));
        }

        return Task.FromResult(Respond(system, user));
    }
}
