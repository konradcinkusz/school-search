using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SchoolSearch.Reviews.DevSeed;

namespace SchoolSearch.Reviews.Tests;

/// <summary>The seed gate itself, without a host: the 404 for Production is decided here, not by the route.</summary>
public sealed class DevSeedOptionsTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "SchoolSearch.Reviews.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Testing", true)]
    public void IsEnabled_is_false_in_production_even_when_enabled_with_a_key(string environmentName, bool expected)
    {
        var options = new DevSeedOptions { Enabled = true, Key = DevSeedFactory.ConfiguredKey };

        var enabled = DevSeedOptions.IsEnabled(options, new FakeHostEnvironment(environmentName));

        Assert.Equal(expected, enabled);
    }
}
