using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SchoolSearch.Seeder;

/// <summary>
/// One seeding run: wait for services, sign in as admin, register or sign in the test accounts, grant the moderator
/// the Admin role, then call the dev seed endpoints. Throws <see cref="SeedFailureException"/> on any failure.
/// Tokens, passwords and the dev seed key are never written to the output.
/// </summary>
public sealed class SeedRunner
{
    public const string DefaultConsentVersion = "2026-01-01";
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromMinutes(3);

    private const int MaxDetailLength = 200;

    private readonly ISeedTransport _transport;
    private readonly TextWriter _output;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<TimeSpan> _elapsed;

    public SeedRunner(ISeedTransport transport, TextWriter output, Func<TimeSpan, CancellationToken, Task> delay, Func<TimeSpan> elapsed)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        _elapsed = elapsed ?? throw new ArgumentNullException(nameof(elapsed));
    }

    public async Task RunAsync(SeederOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        await WaitForServicesAsync(options, cancellationToken);

        ConsentVersions consents = await ReadConsentVersionsAsync(options.AuthBaseUrl, cancellationToken);
        string adminToken = await SignInAdminAsync(options, cancellationToken);

        var subjects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SeedAccount account in SeedAccount.TestAccounts)
        {
            subjects[account.Key] = await EnsureAccountAsync(options, account, consents, cancellationToken);
        }

        foreach (SeedAccount account in SeedAccount.TestAccounts)
        {
            if (account.Role != SeedAccount.UserRole)
            {
                await EnsureRoleAsync(options.AuthBaseUrl, adminToken, subjects[account.Key], account, cancellationToken);
            }
        }

        // Reviews knows all four subjects; billing needs only the owner and the moderator.
        await DevSeedAsync(
            "reviews",
            options.ReviewsBaseUrl,
            options.DevSeedKey,
            subjects,
            new[] { "reviews", "notices", "ownerReplies", "triage" },
            cancellationToken);
        await DevSeedAsync(
            "billing",
            options.BillingBaseUrl,
            options.DevSeedKey,
            Subset(subjects, "owner", "moderator"),
            new[] { "claims", "subscriptions", "profiles", "enquiries" },
            cancellationToken);

        var accounts = new List<SeedAccount>
        {
            new SeedAccount("admin", options.AdminEmail, SeedAccount.SuperAdminRole),
        };
        accounts.AddRange(SeedAccount.TestAccounts);
        _output.WriteLine();
        _output.Write(SummaryFormatter.Format(accounts, options.TestPassword, options.PasswordGenerated));
        _output.WriteLine("Seeding complete.");
    }

    private async Task WaitForServicesAsync(SeederOptions options, CancellationToken cancellationToken)
    {
        var targets = new (string Name, Uri Url)[]
        {
            ("authservice", new Uri(options.AuthBaseUrl, "health/ready")),
            ("reviews", new Uri(options.ReviewsBaseUrl, "health")),
            ("billing", new Uri(options.BillingBaseUrl, "health")),
        };

        _output.WriteLine($"Waiting for services to be ready (up to {ReadinessTimeout.TotalMinutes:0} minutes)...");
        string? notReady = null;
        bool ready = await ReadinessWaiter.WaitAsync(
            async token =>
            {
                foreach ((string name, Uri url) in targets)
                {
                    ApiResponse response = await _transport.SendAsync(SeedRequests.Get(url), token);
                    if (!response.IsSuccess)
                    {
                        notReady = name;
                        return false;
                    }
                }

                notReady = null;
                return true;
            },
            RetryPolicy.Readiness,
            ReadinessTimeout,
            _elapsed,
            _delay,
            cancellationToken);

        if (!ready)
        {
            throw new SeedFailureException(
                $"{notReady ?? "A service"} was not ready within {ReadinessTimeout.TotalMinutes:0} minutes. Check its container logs.");
        }

        _output.WriteLine("All services are ready.");
    }

    private async Task<ConsentVersions> ReadConsentVersionsAsync(Uri authBaseUrl, CancellationToken cancellationToken)
    {
        Uri url = new(authBaseUrl, "api/v1/auth/consents/versions");
        ApiResponse response = await SendWithRetryAsync(() => SeedRequests.Get(url), "consent versions", cancellationToken);
        if (response.IsSuccess && response.Json() is { } root)
        {
            string? terms = JsonFields.GetString(root, "terms");
            string? privacy = JsonFields.GetString(root, "privacy");
            if (terms is not null && privacy is not null)
            {
                return new ConsentVersions(terms, privacy);
            }
        }

        _output.WriteLine($"  Consent versions unavailable ({Describe(response)}); using {DefaultConsentVersion}.");
        return new ConsentVersions(DefaultConsentVersion, DefaultConsentVersion);
    }

    private async Task<string> SignInAdminAsync(SeederOptions options, CancellationToken cancellationToken)
    {
        Uri url = new(options.AuthBaseUrl, "api/v1/auth/login");
        ApiResponse response = await SendWithRetryAsync(
            () => SeedRequests.Post(url, SeedRequests.Login(options.AdminEmail, options.TestPassword)),
            "admin sign-in",
            cancellationToken);
        if (!response.IsSuccess)
        {
            throw new SeedFailureException(
                $"Admin sign-in failed ({Describe(response)}). Check SEED_ADMIN_EMAIL and SEED_TEST_PASSWORD against the authservice InitialAdmin account.");
        }

        _output.WriteLine("Signed in as admin.");
        return AccessTokenOf(response) ?? throw new SeedFailureException("Admin sign-in returned no access token.");
    }

    /// <summary>Registers the account, or signs in when it already exists. Returns the account's subject (JWT "sub").</summary>
    private async Task<string> EnsureAccountAsync(
        SeederOptions options,
        SeedAccount account,
        ConsentVersions consents,
        CancellationToken cancellationToken)
    {
        Uri registerUrl = new(options.AuthBaseUrl, "api/v1/auth/register");
        ApiResponse registered = await SendWithRetryAsync(
            () => SeedRequests.Post(
                registerUrl,
                SeedRequests.Register(account.Email, options.TestPassword, consents.Terms, consents.Privacy)),
            $"register {account.Email}",
            cancellationToken);

        if (registered.IsSuccess)
        {
            string? subject = JwtSubject.TryRead(AccessTokenOf(registered));
            if (subject is not null)
            {
                _output.WriteLine($"  {account.Email}: registered.");
                return subject;
            }

            if (registered.StatusCode == 202)
            {
                // authservice answers 202 with no tokens when email confirmation is required. The local run has no
                // email provider, so this means the local authservice was started with confirmation enforced.
                throw new SeedFailureException(
                    $"Registering {account.Email} needs email verification, so no session was issued. " +
                    "The local authservice must not require a confirmed email (Auth:RequireConfirmedEmail=false, no SendGrid key).");
            }

            // Any other success without a session falls through to sign-in.
        }
        else if (!IsDuplicate(registered))
        {
            throw new SeedFailureException($"Registering {account.Email} failed: {Describe(registered)}.");
        }

        Uri loginUrl = new(options.AuthBaseUrl, "api/v1/auth/login");
        ApiResponse login = await SendWithRetryAsync(
            () => SeedRequests.Post(loginUrl, SeedRequests.Login(account.Email, options.TestPassword)),
            $"sign in {account.Email}",
            cancellationToken);
        if (!login.IsSuccess)
        {
            throw new SeedFailureException(
                $"{account.Email} already exists but signing in failed ({Describe(login)}). " +
                "Its password differs from SEED_TEST_PASSWORD, or the account is not usable. Remove it or reset the local data.");
        }

        string? existingSubject = JwtSubject.TryRead(AccessTokenOf(login));
        if (existingSubject is null)
        {
            throw new SeedFailureException($"Signing in as {account.Email} returned no usable access token.");
        }

        _output.WriteLine($"  {account.Email}: already exists, signed in.");
        return existingSubject;
    }

    /// <summary>Gives the account the role unless it has it already. Reads the user first so re-runs stay quiet.</summary>
    private async Task EnsureRoleAsync(Uri authBaseUrl, string adminToken, string subject, SeedAccount account, CancellationToken cancellationToken)
    {
        string escaped = Uri.EscapeDataString(subject);
        Uri userUrl = new(authBaseUrl, "api/v1/admin/users/" + escaped);
        ApiResponse current = await SendWithRetryAsync(
            () => SeedRequests.Get(userUrl, adminToken),
            $"read {account.Email}",
            cancellationToken);
        if (current.IsSuccess
            && current.Json() is { } user
            && JsonFields.GetStrings(user, "roles").Contains(account.Role, StringComparer.Ordinal))
        {
            _output.WriteLine($"  {account.Email}: already has role {account.Role}.");
            return;
        }

        Uri rolesUrl = new(authBaseUrl, "api/v1/admin/users/" + escaped + "/roles");
        ApiResponse assigned = await SendWithRetryAsync(
            () => SeedRequests.Post(rolesUrl, SeedRequests.AssignRole(account.Role), adminToken),
            $"assign {account.Role} to {account.Email}",
            cancellationToken);
        if (assigned.IsSuccess)
        {
            _output.WriteLine($"  {account.Email}: role {account.Role} assigned.");
            return;
        }

        if (IsDuplicate(assigned))
        {
            _output.WriteLine($"  {account.Email}: already has role {account.Role}.");
            return;
        }

        throw new SeedFailureException($"Assigning role {account.Role} to {account.Email} failed: {Describe(assigned)}.");
    }

    private async Task DevSeedAsync(
        string service,
        Uri baseUrl,
        string devSeedKey,
        IReadOnlyDictionary<string, string> subjects,
        IReadOnlyList<string> countFields,
        CancellationToken cancellationToken)
    {
        Uri url = new(baseUrl, "dev/seed");
        ApiResponse response = await SendWithRetryAsync(
            () => SeedRequests.DevSeed(url, devSeedKey, subjects),
            $"{service} dev seed",
            cancellationToken);
        if (!response.IsSuccess)
        {
            throw new SeedFailureException(DevSeedFailure(service, response));
        }

        if (response.Json() is not { } root)
        {
            throw new SeedFailureException($"{service} /dev/seed returned a body that is not a JSON object.");
        }

        var counts = new List<string>();
        foreach (string field in countFields)
        {
            int? count = JsonFields.GetInt(root, field);
            if (count is null)
            {
                throw new SeedFailureException($"{service} /dev/seed response has no '{field}' count.");
            }

            counts.Add($"{field}={count.Value}");
        }

        bool? alreadySeeded = JsonFields.GetBool(root, "alreadySeeded");
        if (alreadySeeded is null)
        {
            throw new SeedFailureException($"{service} /dev/seed response has no 'alreadySeeded' flag.");
        }

        _output.WriteLine($"  {service} /dev/seed: {string.Join(", ", counts)}, alreadySeeded={(alreadySeeded.Value ? "true" : "false")}");
    }

    /// <summary>Retries rate limiting (429), temporary unavailability (503) and network failures, honouring Retry-After.</summary>
    private async Task<ApiResponse> SendWithRetryAsync(Func<HttpRequestMessage> build, string label, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            ApiResponse response = await _transport.SendAsync(build(), cancellationToken);
            if (!response.IsTransient || attempt + 1 >= RetryPolicy.Requests.MaxAttempts)
            {
                return response;
            }

            TimeSpan wait = RetryPolicy.Requests.DelayFor(attempt, response.RetryAfter);
            _output.WriteLine($"  {label}: {response}, retrying in {wait.TotalSeconds:0.#} s.");
            await _delay(wait, cancellationToken);
        }
    }

    private static string DevSeedFailure(string service, ApiResponse response)
    {
        return response.StatusCode switch
        {
            401 or 403 => $"{service} rejected the dev seed key ({response}). DEV_SEED_KEY must equal the service's DevSeed__Key.",
            404 => $"{service} /dev/seed is not available (HTTP 404). It needs DevSeed__Enabled=true and a non-Production environment.",
            _ => $"{service} /dev/seed failed: {Describe(response)}.",
        };
    }

    private static bool IsDuplicate(ApiResponse response)
    {
        if (response.StatusCode == 409)
        {
            return true;
        }

        return response.StatusCode == 400
            && Messages(response).Any(message =>
                message.Contains("already", StringComparison.OrdinalIgnoreCase)
                || message.Contains("taken", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Status plus the service's own short messages. The raw body is never included.</summary>
    private static string Describe(ApiResponse response)
    {
        List<string> messages = Messages(response);
        if (messages.Count == 0)
        {
            return response.ToString();
        }

        string detail = string.Join("; ", messages);
        if (detail.Length > MaxDetailLength)
        {
            detail = detail.Substring(0, MaxDetailLength) + "...";
        }

        return $"{response}: {detail}";
    }

    private static List<string> Messages(ApiResponse response)
    {
        return response.Json() is { } root ? JsonFields.Messages(root) : new List<string>();
    }

    private static string? AccessTokenOf(ApiResponse response)
    {
        return response.Json() is { } root ? JsonFields.GetString(root, "accessToken") : null;
    }

    private static Dictionary<string, string> Subset(IReadOnlyDictionary<string, string> source, params string[] keys)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            result[key] = source[key];
        }

        return result;
    }

    private sealed record ConsentVersions(string Terms, string Privacy);
}
