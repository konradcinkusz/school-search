using System;
using System.Collections.Generic;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

public sealed class SeederOptionsTests
{
    [Fact]
    public void Missing_required_variables_are_all_reported()
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);

        SeederConfigurationException ex = Assert.Throws<SeederConfigurationException>(
            () => SeederOptions.FromEnvironment(SeedFixtures.Lookup(empty)));

        Assert.Contains("AUTH_BASE_URL", ex.Message);
        Assert.Contains("REVIEWS_BASE_URL", ex.Message);
        Assert.Contains("BILLING_BASE_URL", ex.Message);
        Assert.Contains("DEV_SEED_KEY", ex.Message);
        Assert.Contains("SEED_ADMIN_EMAIL", ex.Message);
        Assert.Contains("SEED_TEST_PASSWORD", ex.Message);
    }

    [Fact]
    public void Valid_environment_parses_with_generated_flag_defaulting_to_false()
    {
        SeederOptions options = SeedFixtures.Options();

        Assert.Equal("http://auth.test/", options.AuthBaseUrl.AbsoluteUri);
        Assert.Equal("http://reviews.test/", options.ReviewsBaseUrl.AbsoluteUri);
        Assert.Equal("http://billing.test/", options.BillingBaseUrl.AbsoluteUri);
        Assert.Equal(SeedFixtures.DevSeedKey, options.DevSeedKey);
        Assert.Equal(SeedFixtures.AdminEmail, options.AdminEmail);
        Assert.Equal(SeedFixtures.TestPassword, options.TestPassword);
        Assert.False(options.PasswordGenerated);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData(" True ")]
    public void Generated_flag_accepts_true_in_any_case(string value)
    {
        SeederOptions options = SeedFixtures.Options(value);

        Assert.True(options.PasswordGenerated);
    }

    [Fact]
    public void Generated_flag_set_to_false_is_false()
    {
        SeederOptions options = SeedFixtures.Options("false");

        Assert.False(options.PasswordGenerated);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    public void Generated_flag_rejects_other_values(string value)
    {
        SeederConfigurationException ex = Assert.Throws<SeederConfigurationException>(
            () => SeedFixtures.Options(value));

        Assert.Contains("SEED_PASSWORD_GENERATED", ex.Message);
    }

    [Fact]
    public void Base_urls_are_normalised_to_end_with_a_slash()
    {
        var values = SeedFixtures.ValidEnvironment();
        values["AUTH_BASE_URL"] = "http://auth.test/base///";

        SeederOptions options = SeederOptions.FromEnvironment(SeedFixtures.Lookup(values));

        Assert.Equal("http://auth.test/base/", options.AuthBaseUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("auth.test")]
    [InlineData("ftp://auth.test")]
    [InlineData("not a url")]
    public void Base_url_must_be_absolute_http_or_https(string value)
    {
        var values = SeedFixtures.ValidEnvironment();
        values["AUTH_BASE_URL"] = value;

        SeederConfigurationException ex = Assert.Throws<SeederConfigurationException>(
            () => SeederOptions.FromEnvironment(SeedFixtures.Lookup(values)));

        Assert.Contains("AUTH_BASE_URL must be an absolute http or https URL", ex.Message);
    }

    [Fact]
    public void Blank_password_is_rejected()
    {
        var values = SeedFixtures.ValidEnvironment();
        values["SEED_TEST_PASSWORD"] = "   ";

        SeederConfigurationException ex = Assert.Throws<SeederConfigurationException>(
            () => SeederOptions.FromEnvironment(SeedFixtures.Lookup(values)));

        Assert.Contains("SEED_TEST_PASSWORD", ex.Message);
    }

    [Fact]
    public void Admin_email_without_an_at_sign_is_rejected()
    {
        var values = SeedFixtures.ValidEnvironment();
        values["SEED_ADMIN_EMAIL"] = "not-an-email";

        SeederConfigurationException ex = Assert.Throws<SeederConfigurationException>(
            () => SeederOptions.FromEnvironment(SeedFixtures.Lookup(values)));

        Assert.Contains("SEED_ADMIN_EMAIL must be an email address", ex.Message);
    }

    [Fact]
    public void ToString_never_shows_the_key_or_the_password()
    {
        SeederOptions options = SeedFixtures.Options();

        string text = options.ToString();

        Assert.DoesNotContain(SeedFixtures.DevSeedKey, text);
        Assert.DoesNotContain(SeedFixtures.TestPassword, text);
    }
}
