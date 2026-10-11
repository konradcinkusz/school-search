using Microsoft.Extensions.Options;

namespace SchoolSearch.Api.Ai;

/// <summary>
/// Process-wide daily token counter (input plus output, all explains). Resets at UTC midnight. A call is allowed while
/// the day's spend is below the budget; the last call of the day may overshoot by one call's usage, which is bounded
/// by MaxOutputTokens plus the prompt size. State is in memory: a restart starts the day again (cost guard, not billing).
/// </summary>
public sealed class TokenBudget(TimeProvider clock, IOptions<AiOptions> options)
{
    private readonly object _gate = new();
    private DateOnly _day;
    private long _spent;

    public bool IsExhausted()
    {
        lock (_gate)
        {
            RollToToday();
            return _spent >= options.Value.DailyTokenBudget;
        }
    }

    public void Record(long tokens)
    {
        if (tokens <= 0)
        {
            return;
        }

        lock (_gate)
        {
            RollToToday();
            _spent += tokens;
        }
    }

    private void RollToToday()
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (today != _day)
        {
            _day = today;
            _spent = 0;
        }
    }
}
