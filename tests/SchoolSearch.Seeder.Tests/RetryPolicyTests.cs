using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

public sealed class RetryPolicyTests
{
    [Fact]
    public void Backoff_doubles_and_stops_at_the_cap()
    {
        var policy = new RetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), 6);

        TimeSpan[] delays = Enumerable.Range(0, 6).Select(attempt => policy.DelayFor(attempt)).ToArray();

        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10),
            },
            delays);
    }

    [Fact]
    public void Request_policy_schedule_is_2_4_8_16_then_capped_at_30()
    {
        TimeSpan[] delays = Enumerable.Range(0, 6).Select(attempt => RetryPolicy.Requests.DelayFor(attempt)).ToArray();

        Assert.Equal(
            new[]
            {
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(8),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30),
            },
            delays);
    }

    [Fact]
    public void Very_large_attempt_numbers_do_not_overflow()
    {
        TimeSpan delay = RetryPolicy.Readiness.DelayFor(100_000);

        Assert.Equal(RetryPolicy.Readiness.MaxDelay, delay);
    }

    [Fact]
    public void Server_hint_replaces_the_computed_delay()
    {
        TimeSpan delay = RetryPolicy.Requests.DelayFor(0, TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), delay);
    }

    [Fact]
    public void Server_hint_is_capped()
    {
        TimeSpan delay = RetryPolicy.Requests.DelayFor(0, TimeSpan.FromHours(1));

        Assert.Equal(RetryPolicy.MaxServerHint, delay);
    }

    [Fact]
    public void Zero_server_hint_is_ignored()
    {
        TimeSpan delay = RetryPolicy.Requests.DelayFor(1, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(4), delay);
    }

    [Fact]
    public void Negative_attempt_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RetryPolicy.Requests.DelayFor(-1));
    }

    [Fact]
    public async Task Readiness_succeeds_on_the_third_probe_after_two_waits()
    {
        var clock = new VirtualClock();
        var answers = new Queue<bool>(new[] { false, false, true });

        bool ready = await ReadinessWaiter.WaitAsync(
            _ => Task.FromResult(answers.Dequeue()),
            RetryPolicy.Readiness,
            TimeSpan.FromMinutes(3),
            () => clock.Now,
            clock.Delay,
            CancellationToken.None);

        Assert.True(ready);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, clock.Delays);
    }

    [Fact]
    public async Task Readiness_gives_up_when_the_budget_is_spent()
    {
        var clock = new VirtualClock();
        int probes = 0;

        bool ready = await ReadinessWaiter.WaitAsync(
            _ =>
            {
                probes++;
                return Task.FromResult(false);
            },
            RetryPolicy.Readiness,
            TimeSpan.FromSeconds(3),
            () => clock.Now,
            clock.Delay,
            CancellationToken.None);

        Assert.False(ready);
        Assert.Equal(3, probes);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, clock.Delays);
    }

    [Fact]
    public async Task Last_wait_is_cut_to_the_remaining_budget()
    {
        var clock = new VirtualClock();

        bool ready = await ReadinessWaiter.WaitAsync(
            _ => Task.FromResult(false),
            RetryPolicy.Readiness,
            TimeSpan.FromSeconds(2.5),
            () => clock.Now,
            clock.Delay,
            CancellationToken.None);

        Assert.False(ready);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5) }, clock.Delays);
    }
}
