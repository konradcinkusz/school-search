using SchoolSearch.Billing.DevSeed;

namespace SchoolSearch.Billing.Tests.DevSeed;

/// <summary>The access rule of POST /dev/seed, as a pure function (no host needed).</summary>
public sealed class DevSeedGateTests
{
    [Theory]
    [InlineData(false, "k", false, "k", DevSeedDecision.NotFound)]
    [InlineData(true, null, false, "k", DevSeedDecision.NotFound)]
    [InlineData(true, "   ", false, "   ", DevSeedDecision.NotFound)]
    [InlineData(true, "k", true, "k", DevSeedDecision.NotFound)]
    [InlineData(true, "k", false, null, DevSeedDecision.Unauthorized)]
    [InlineData(true, "k", false, "", DevSeedDecision.Unauthorized)]
    [InlineData(true, "k", false, "wrong", DevSeedDecision.Unauthorized)]
    [InlineData(true, "k", false, "kk", DevSeedDecision.Unauthorized)]
    [InlineData(true, "k", false, "k", DevSeedDecision.Allowed)]
    public void Decide_applies_switch_environment_and_key_in_that_order(
        bool enabled,
        string? configuredKey,
        bool isProduction,
        string? presentedKey,
        DevSeedDecision expected)
    {
        Assert.Equal(expected, DevSeedGate.Decide(enabled, configuredKey, isProduction, presentedKey));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData(" true ", true)]
    [InlineData("false", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEnabled_reads_only_a_true_value(string? configuredValue, bool expected)
    {
        Assert.Equal(expected, DevSeedGate.IsEnabled(configuredValue));
    }

    [Fact]
    public void KeysMatch_is_exact_and_length_sensitive()
    {
        Assert.True(DevSeedGate.KeysMatch("abc", "abc"));
        Assert.False(DevSeedGate.KeysMatch("abc", "abd"));
        Assert.False(DevSeedGate.KeysMatch("abc", "ab"));
        Assert.False(DevSeedGate.KeysMatch("abc", "abcd"));
    }
}
