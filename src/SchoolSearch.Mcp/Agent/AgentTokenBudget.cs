namespace SchoolSearch.Mcp.Agent;

/// <summary>
/// Process-wide daily token counter for the agent (input plus output, all requests). Resets at UTC midnight. A call is
/// allowed while the day's spend is below the budget. State is in memory: a restart starts the day again (cost guard,
/// not billing).
/// </summary>
public sealed class AgentTokenBudget(AgentOptions options, TimeProvider clock)
{
    private readonly object _gate = new();
    private DateOnly _day;
    private long _spent;

    public bool IsExhausted()
    {
        lock (_gate)
        {
            RollToToday();
            return _spent >= options.DailyTokenBudget;
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
