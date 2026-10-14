using SchoolSearch.Api.Ai;

namespace SchoolSearch.Api.Tests.Ai;

/// <summary>
/// Stands in for the model. Records every call so tests can prove whether the model path was taken, and answers with
/// <see cref="Respond"/>. The default answer is a neutral JSON object with no filters set.
/// </summary>
public sealed class SearchIntentFakeClient : ISearchIntentClient
{
    public const string NeutralJson = "{\"gmina\":null,\"q\":null,\"publicOnly\":null,\"maxDistanceKm\":null,\"type\":null,\"kind\":null,\"maxTuitionPln\":null,\"hasFreePlaces\":null,\"place\":null,\"unsupportedKinds\":[]}";

    private readonly object _gate = new();
    private readonly List<(string System, string User)> _prompts = new();
    private int _calls;

    public Func<string, string, SearchIntentCompletion> Respond { get; set; } =
        (_, _) => new SearchIntentCompletion(NeutralJson, 100, 50);

    /// <summary>When set, the call fails with this exception instead of answering.</summary>
    public Exception? Fail { get; set; }

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

    public Task<SearchIntentCompletion> CompleteAsync(string system, string user, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        lock (_gate)
        {
            _prompts.Add((system, user));
        }

        if (Fail is not null)
        {
            throw Fail;
        }

        return Task.FromResult(Respond(system, user));
    }
}
