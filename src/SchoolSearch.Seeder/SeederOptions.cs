using System;
using System.Collections.Generic;

namespace SchoolSearch.Seeder;

/// <summary>
/// Configuration for one seeding run, read from environment variables. Base URLs are normalised to end with "/".
/// The record's generated ToString is replaced so that secrets never reach a log through an accidental interpolation.
/// </summary>
public sealed record SeederOptions(
    Uri AuthBaseUrl,
    Uri ReviewsBaseUrl,
    Uri BillingBaseUrl,
    string DevSeedKey,
    string AdminEmail,
    string TestPassword,
    bool PasswordGenerated)
{
    public const string AuthBaseUrlVariable = "AUTH_BASE_URL";
    public const string ReviewsBaseUrlVariable = "REVIEWS_BASE_URL";
    public const string BillingBaseUrlVariable = "BILLING_BASE_URL";
    public const string DevSeedKeyVariable = "DEV_SEED_KEY";
    public const string AdminEmailVariable = "SEED_ADMIN_EMAIL";
    public const string TestPasswordVariable = "SEED_TEST_PASSWORD";
    public const string PasswordGeneratedVariable = "SEED_PASSWORD_GENERATED";

    /// <summary>Reads and validates the configuration. Throws <see cref="SeederConfigurationException"/> listing every problem.</summary>
    public static SeederOptions FromEnvironment(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var problems = new List<string>();

        Uri? authBaseUrl = ReadBaseUrl(environment, AuthBaseUrlVariable, problems);
        Uri? reviewsBaseUrl = ReadBaseUrl(environment, ReviewsBaseUrlVariable, problems);
        Uri? billingBaseUrl = ReadBaseUrl(environment, BillingBaseUrlVariable, problems);
        string? devSeedKey = ReadRequired(environment, DevSeedKeyVariable, problems);
        string? adminEmail = ReadRequired(environment, AdminEmailVariable, problems);
        string? testPassword = ReadSecret(environment, TestPasswordVariable, problems);
        bool? passwordGenerated = ReadFlag(environment, PasswordGeneratedVariable, problems);

        if (adminEmail is not null && !LooksLikeEmail(adminEmail))
        {
            problems.Add($"{AdminEmailVariable} must be an email address.");
        }

        if (problems.Count > 0
            || authBaseUrl is null
            || reviewsBaseUrl is null
            || billingBaseUrl is null
            || devSeedKey is null
            || adminEmail is null
            || testPassword is null
            || passwordGenerated is null)
        {
            throw new SeederConfigurationException(string.Join(Environment.NewLine, problems));
        }

        return new SeederOptions(
            authBaseUrl,
            reviewsBaseUrl,
            billingBaseUrl,
            devSeedKey,
            adminEmail,
            testPassword,
            passwordGenerated.Value);
    }

    /// <summary>Never prints the dev seed key or the password.</summary>
    public override string ToString() => "SeederOptions(secrets redacted)";

    private static string? ReadRequired(Func<string, string?> environment, string name, List<string> problems)
    {
        string? value = environment(name)?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            problems.Add($"{name} is required but is not set.");
            return null;
        }

        return value;
    }

    /// <summary>Passwords are taken as they are; only a blank value is rejected.</summary>
    private static string? ReadSecret(Func<string, string?> environment, string name, List<string> problems)
    {
        string? value = environment(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{name} is required but is not set.");
            return null;
        }

        return value;
    }

    private static Uri? ReadBaseUrl(Func<string, string?> environment, string name, List<string> problems)
    {
        string? raw = ReadRequired(environment, name, problems);
        if (raw is null)
        {
            return null;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            problems.Add($"{name} must be an absolute http or https URL.");
            return null;
        }

        return new Uri(raw.TrimEnd('/') + "/", UriKind.Absolute);
    }

    /// <summary>Unset or empty means false. Accepts "true" or "false" in any letter case.</summary>
    private static bool? ReadFlag(Func<string, string?> environment, string name, List<string> problems)
    {
        string? raw = environment(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        problems.Add($"{name} must be 'true' or 'false' when set.");
        return null;
    }

    private static bool LooksLikeEmail(string value)
    {
        int at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && value.IndexOf(' ') < 0;
    }
}
