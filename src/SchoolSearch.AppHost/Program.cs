// Composition root for local development (P1). This is NOT the production topology:
// production is described by flyio/*.fly.toml and the tag-driven workflow.
// No secret literal lives here. Aspire generates a throwaway password for the local Postgres
// container, so a fresh clone needs zero credentials (P8).
// The demo seed generates its own per-run secrets (LocalSeedSupport.cs); none is written to disk or logged.
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// SeedDemo (default true) turns on the local demo data: the API demo catalog, the reviews and billing dev seeds,
// the authservice test accounts and the seeder resource. Local only; the API refuses the demo seed in Production.
// Set SeedDemo=false for the zero-credential run.
var seedDemoSetting = builder.Configuration["SeedDemo"];
var seedDemo = string.IsNullOrWhiteSpace(seedDemoSetting)
    || (bool.TryParse(seedDemoSetting, out var seedDemoValue) && seedDemoValue);

// authservice is the external identity provider (ADR-0004, ADR-0015), run here as a container. Its default follows
// SeedDemo, so the zero-credential run (SeedDemo=false) leaves it off. An explicit IncludeAuthservice value always wins.
// Without SeedDemo it needs a user secret Authservice:JwtPrivateKeyPem (RS256 PEM, one line with literal \n escapes):
// without it authservice falls back to HS256, its JWKS stays empty, and no consumer can validate a token.
var includeAuthserviceSetting = builder.Configuration["IncludeAuthservice"];
var includeAuthservice = string.IsNullOrWhiteSpace(includeAuthserviceSetting)
    ? seedDemo
    : bool.TryParse(includeAuthserviceSetting, out var include) && include;

// Demo seed values, resolved per value in this order: explicit configuration (user-secrets), then the local store
// .local/seed-secrets.json, then generated and written to that store. The Postgres volume persists across runs, so the
// stored values must come back on the next run. Nothing here is logged; the seeder prints the password when it is not
// configured. Everything below is set only when SeedDemo is on, except the peppers, which keep their per-run default.
var seedAdminEmail = LocalSeedSupport.OrDefault(builder.Configuration["Seed:AdminEmail"], "admin@school-search.test");
var seedStorePath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".local", "seed-secrets.json"));
var configuredJwtKey = LocalSeedSupport.NonBlank(builder.Configuration["Authservice:JwtPrivateKeyPem"]);
var configuredReviewsPepper = LocalSeedSupport.NonBlank(builder.Configuration["Reviews:SubjectPepper"]);
var configuredBillingPepper = LocalSeedSupport.NonBlank(builder.Configuration["Billing:SubjectPepper"]);
var configuredTestPassword = LocalSeedSupport.NonBlank(builder.Configuration["Seed:TestPassword"]);

// Web sign-in secrets (Phase M). SESSION_SECRET seals the ss_session cookie of the web BFF (at least 32 bytes, otherwise
// every account route answers 503). BFF_SHARED_SECRET is the same value in web, api, reviews, billing and mcp: it lets
// the services trust the visitor address the BFF forwards. Same resolution as the other seed secrets: configuration
// (user-secrets or environment), then the local store, then generated. A configured SESSION_SECRET that is too short is
// ignored with a warning in the banner, because passing it on would silently disable the account routes.
var configuredSessionSecret = LocalSeedSupport.NonBlank(builder.Configuration["SESSION_SECRET"]);
string? sessionSecretWarning = null;
if (configuredSessionSecret is not null && !LocalSeedSupport.IsUsableSessionSecret(configuredSessionSecret))
{
    sessionSecretWarning = $"SESSION_SECRET in configuration is shorter than {LocalSeedSupport.SessionSecretMinimumBytes} bytes and is ignored.";
    configuredSessionSecret = null;
}

var configuredBffSharedSecret = LocalSeedSupport.NonBlank(builder.Configuration["BFF_SHARED_SECRET"]);

// Optional Google Maps key, passed to the web resource only when present (the map page reads it at runtime).
var googleMapsApiKey = LocalSeedSupport.NonBlank(builder.Configuration["GOOGLE_MAPS_API_KEY"]);

var seedTestPassword = string.Empty;
var seedPasswordSource = "none"; // "configuration", "store" or "generated"
var devSeedKey = string.Empty;
var jwtKey = configuredJwtKey;
var jwtKeySource = configuredJwtKey is null ? "none" : "configuration";
string reviewsPepper;
string billingPepper;
string? seedStoreProblem = null;
string? sessionSecret = configuredSessionSecret;
var sessionSecretSource = configuredSessionSecret is null ? "none" : "configuration"; // "store", "generated" or "per run" too
string? bffSharedSecret = configuredBffSharedSecret;

if (seedDemo)
{
    var store = LocalSeedStore.Open(seedStorePath);
    var secrets = store.Data;

    if (configuredTestPassword is not null)
    {
        seedTestPassword = configuredTestPassword;
        seedPasswordSource = "configuration";
    }
    else if (secrets.TestPassword is not null)
    {
        seedTestPassword = secrets.TestPassword;
        seedPasswordSource = "store";
    }
    else
    {
        seedTestPassword = LocalSeedSupport.CreatePassword();
        secrets.TestPassword = seedTestPassword;
        store.Changed = true;
        seedPasswordSource = "generated";
    }

    var storedDevSeedKey = secrets.DevSeedKey ?? LocalSeedSupport.CreateDevSeedKey();
    if (secrets.DevSeedKey is null)
    {
        secrets.DevSeedKey = storedDevSeedKey;
        store.Changed = true;
    }

    devSeedKey = storedDevSeedKey;

    reviewsPepper = configuredReviewsPepper ?? secrets.ReviewsPepper ?? LocalSeedSupport.CreatePepper();
    if (configuredReviewsPepper is null && secrets.ReviewsPepper is null)
    {
        secrets.ReviewsPepper = reviewsPepper;
        store.Changed = true;
    }

    billingPepper = configuredBillingPepper ?? secrets.BillingPepper ?? LocalSeedSupport.CreatePepper();
    if (configuredBillingPepper is null && secrets.BillingPepper is null)
    {
        secrets.BillingPepper = billingPepper;
        store.Changed = true;
    }

    // The authservice RS256 key is needed only when authservice is included. Configuration wins over the store.
    if (jwtKey is null && includeAuthservice)
    {
        if (secrets.JwtPrivateKeyPem is not null)
        {
            jwtKey = secrets.JwtPrivateKeyPem;
            jwtKeySource = "store";
        }
        else
        {
            jwtKey = LocalSeedSupport.CreateJwtPrivateKeyPem();
            secrets.JwtPrivateKeyPem = jwtKey;
            store.Changed = true;
            jwtKeySource = "generated";
        }
    }

    // The session secret matters only when authservice is included (the web accounts need AUTH_BASE_URL as well).
    // A stored value that is too short (a hand-edited file) is replaced by a generated one.
    if (sessionSecret is null && includeAuthservice)
    {
        if (LocalSeedSupport.IsUsableSessionSecret(secrets.SessionSecret))
        {
            sessionSecret = secrets.SessionSecret;
            sessionSecretSource = "store";
        }
        else
        {
            sessionSecret = LocalSeedSupport.CreateSessionSecret();
            secrets.SessionSecret = sessionSecret;
            store.Changed = true;
            sessionSecretSource = "generated";
        }
    }

    if (bffSharedSecret is null)
    {
        if (LocalSeedSupport.NonBlank(secrets.BffSharedSecret) is { } storedBffSecret)
        {
            bffSharedSecret = storedBffSecret;
        }
        else
        {
            bffSharedSecret = LocalSeedSupport.CreateBffSharedSecret();
            secrets.BffSharedSecret = bffSharedSecret;
            store.Changed = true;
        }
    }

    seedStoreProblem = store.TrySave();
}
else
{
    // Without SeedDemo the peppers keep their per-run default, as before.
    reviewsPepper = configuredReviewsPepper ?? LocalSeedSupport.CreatePepper();
    billingPepper = configuredBillingPepper ?? LocalSeedSupport.CreatePepper();

    // An authservice started without SeedDemo still needs a session secret for the web sign-in. There is no store here,
    // so it is random for each run (sessions end when the AppHost restarts). The shared secret stays unset unless configured.
    if (sessionSecret is null && includeAuthservice)
    {
        sessionSecret = LocalSeedSupport.CreateSessionSecret();
        sessionSecretSource = "per run";
    }
}

// AI passthrough (P8): set only when configured. Without ANTHROPIC_API_KEY the AI features degrade.
var anthropicApiKey = LocalSeedSupport.NonBlank(builder.Configuration["ANTHROPIC_API_KEY"]);
var aiEnabled = LocalSeedSupport.NonBlank(builder.Configuration["AI_ENABLED"]);

// Local values only: public issuer and audience, and a fixed host port so the public base URL is known in advance.
const string AuthBaseUrl = "http://localhost:8090";
const string AuthIssuer = "school-search";
const string AuthAudience = "school-search";

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("school-search-postgres-data");

var catalogDb = postgres.AddDatabase("catalogdb");
var reviewsDb = postgres.AddDatabase("reviewsdb");

// The API owns catalogdb. DATABASE_PROVIDER=postgres selects Npgsql; without a connection
// string the kernel falls back to InMemory (P8), so this edge is the only wiring needed.
var api = builder.AddProject<Projects.SchoolSearch_Api>("api")
    .WithEnvironment("DATABASE_PROVIDER", "postgres")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithHttpHealthCheck("/health");

if (seedDemo)
{
    // The API loads its synthetic demo catalog. The API refuses this flag in Production.
    api = api.WithEnvironment("SCHOOLSEARCH_DEMO_SEED", "true");
}

if (anthropicApiKey is not null)
{
    api = LocalSeedSupport.WithAiSettings(api, anthropicApiKey, aiEnabled);
}

// The Reviews service owns reviewsdb (database per service). Its subject pepper keys the author hashes. It comes from
// Reviews:SubjectPepper (user-secrets), else the local store when SeedDemo is on, else a random pepper per run.
var reviews = builder.AddProject<Projects.SchoolSearch_Reviews>("reviews")
    .WithEnvironment("Reviews__SubjectPepper", reviewsPepper)
    .WithReference(reviewsDb)
    .WaitFor(reviewsDb)
    .WithHttpHealthCheck("/health");

if (seedDemo)
{
    // Enables POST /dev/seed. It also needs the X-Dev-Seed-Key header and a non-Production environment.
    reviews = reviews
        .WithEnvironment("DevSeed__Enabled", "true")
        .WithEnvironment("DevSeed__Key", devSeedKey);
}

if (anthropicApiKey is not null)
{
    reviews = LocalSeedSupport.WithAiSettings(reviews, anthropicApiKey, aiEnabled);
}

// Billing (Phase F, ADR-0016): premium school profiles, parent enquiries and subscription entitlements. It owns billingdb
// (database per service). It is added only once its project exists, so the solution builds before the service lands
// (the same rule as the Reviews reference in the architecture test project). The path overload is used because the
// generated Projects.* type exists only when the project is referenced, and this AppHost does not reference it.
// Mock mode is stated on purpose: the synthetic checkout and the simulate endpoint are local-only (MONETIZATION-BRIEF 1c).
var billingProject = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SchoolSearch.Billing", "SchoolSearch.Billing.csproj"));
IResourceBuilder<ProjectResource>? billing = null;
if (File.Exists(billingProject))
{
    var billingDb = postgres.AddDatabase("billingdb");

    // Keys the subject hash of claims and subscriptions (configuration, else the local store with SeedDemo, else per run).
    billing = builder.AddProject("billing", billingProject)
        .WithEnvironment("Billing__MockMode", "true")
        .WithEnvironment("Billing__SubjectPepper", billingPepper)
        .WithReference(billingDb)
        .WaitFor(billingDb)
        .WithHttpHealthCheck("/health");

    if (seedDemo)
    {
        // Enables POST /dev/seed (same gate as reviews).
        billing = billing
            .WithEnvironment("DevSeed__Enabled", "true")
            .WithEnvironment("DevSeed__Key", devSeedKey);
    }

    if (anthropicApiKey is not null)
    {
        billing = LocalSeedSupport.WithAiSettings(billing, anthropicApiKey, aiEnabled);
    }

    // Optional. Only meaningful once a payment provider adapter exists (ADR-0016). Without it, mock mode accepts
    // requests locally; outside mock mode the service rejects unsigned webhooks.
    var billingWebhookSecret = builder.Configuration["Billing:WebhookSecret"];
    if (!string.IsNullOrEmpty(billingWebhookSecret))
    {
        billing = billing.WithEnvironment("Billing__WebhookSecret", billingWebhookSecret);
    }
}

// authservice is declared here so the seeder can wait for it below. It stays null when authservice is not included.
IResourceBuilder<ContainerResource>? authservice = null;
if (includeAuthservice)
{
    var authDb = postgres.AddDatabase("authservicedb");

    // The key comes from configuration, else the local store or a generated one when SeedDemo is on (resolved above).
    var jwtPemForAuthservice = jwtKey
        ?? throw new InvalidOperationException(
            "IncludeAuthservice=true needs the user secret Authservice:JwtPrivateKeyPem (RS256 PEM). See docs/runbooks/AUTHSERVICE.md section 5.");

    // Pinned tag, never :latest (flyio/authservice.fly.toml uses the same image). Dev only: production runs on Fly.
    authservice = builder.AddContainer("authservice", "ghcr.io/konradcinkusz/authservice", "v0.3.4")
        .WithHttpEndpoint(port: 8090, targetPort: 8080, name: "http")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
        .WithEnvironment("DatabaseProvider", "PostgreSQL")
        .WithEnvironment("Jwt__Issuer", AuthIssuer)
        .WithEnvironment("Jwt__Audience", AuthAudience)
        .WithEnvironment("Jwt__PublicBaseUrl", AuthBaseUrl)
        .WithEnvironment("Jwt__PrivateKeyPem", jwtPemForAuthservice)
        .WithEnvironment("Database__SchemaMode", "Migrate")
        .WithEnvironment("Database__MigrationsAssembly", "AuthService.Migrations.PostgreSQL")
        .WithEnvironment("ConnectionStrings__DefaultConnection", authDb.Resource.ConnectionStringExpression)
        .WaitFor(authDb);

    if (seedDemo)
    {
        // Creates the SuperAdmin on first start. Its password is the test password, so the seeder can log in as admin.
        authservice = authservice
            .WithEnvironment("InitialAdmin__Email", seedAdminEmail)
            .WithEnvironment("InitialAdmin__Password", seedTestPassword);
    }

    // Consumers validate tokens against authservice (Auth:Authority, Auth:Issuer, Auth:Audience). Same values as above.
    api = api
        .WithEnvironment("Auth__Authority", AuthBaseUrl)
        .WithEnvironment("Auth__Issuer", AuthIssuer)
        .WithEnvironment("Auth__Audience", AuthAudience)
        .WaitFor(authservice);
    reviews = reviews
        .WithEnvironment("Auth__Authority", AuthBaseUrl)
        .WithEnvironment("Auth__Issuer", AuthIssuer)
        .WithEnvironment("Auth__Audience", AuthAudience)
        .WaitFor(authservice);
    if (billing is not null)
    {
        billing = billing
            .WithEnvironment("Auth__Authority", AuthBaseUrl)
            .WithEnvironment("Auth__Issuer", AuthIssuer)
            .WithEnvironment("Auth__Audience", AuthAudience)
            .WaitFor(authservice);
    }
}

// The MCP service (ADR-0006) hosts the Phase G agent "Doradca wyboru szkoły" (ADR-0017). It owns no database and reads
// the catalog through the API and the reviews through the Reviews service, both over HTTP. Added only once its project
// exists, the same rule as Billing. Its AI kill switch and key are not set here: without ANTHROPIC_API_KEY the agent
// answers 503 ai_unavailable (P8). The path overload is used because this AppHost does not reference the MCP project.
var mcpProject = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SchoolSearch.Mcp", "SchoolSearch.Mcp.csproj"));
IResourceBuilder<ProjectResource>? mcp = null;
if (File.Exists(mcpProject))
{
    mcp = builder.AddProject("mcp", mcpProject)
        .WithEnvironment("CATALOG_API_URL", api.GetEndpoint("http"))
        .WithEnvironment("REVIEWS_BASE_URL", reviews.GetEndpoint("http"))
        .WaitFor(api)
        .WaitFor(reviews)
        .WithHttpHealthCheck("/health");

    if (anthropicApiKey is not null)
    {
        mcp = LocalSeedSupport.WithAiSettings(mcp, anthropicApiKey, aiEnabled);
    }
}

// BFF_SHARED_SECRET: the same value in every service the web BFF calls, so the forwarded visitor address is trusted
// (per-visitor rate limits). Optional for sign-in itself; set whenever a value exists (configuration or the local store).
if (bffSharedSecret is not null)
{
    api = api.WithEnvironment("BFF_SHARED_SECRET", bffSharedSecret);
    reviews = reviews.WithEnvironment("BFF_SHARED_SECRET", bffSharedSecret);
    if (billing is not null)
    {
        billing = billing.WithEnvironment("BFF_SHARED_SECRET", bffSharedSecret);
    }

    if (mcp is not null)
    {
        mcp = mcp.WithEnvironment("BFF_SHARED_SECRET", bffSharedSecret);
    }
}

// The product surface. It reads the API through its server-side proxy; the proxy's candidate
// ladder picks up services__api__http__0, which WithReference injects.
var webAppDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "web", "app"));

// Requires a "dev" script in web/app/package.json that honours the PORT environment variable.
// The BFF calls the Reviews service server-side; REVIEWS_BASE_URL is read per request (web/app/src/lib).
var web = builder.AddJavaScriptApp("web", webAppDirectory, "dev")
    .WithHttpEndpoint(env: "PORT")
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("REVIEWS_BASE_URL", reviews.GetEndpoint("http"))
    .WaitFor(reviews)
    .WithHttpHealthCheck("/api/config");

if (billing is not null)
{
    // The BFF calls the Billing service server-side (BILLING_BASE_URL is read per request, like REVIEWS_BASE_URL).
    web = web.WithEnvironment("BILLING_BASE_URL", billing.GetEndpoint("http"))
        .WaitFor(billing);
}

if (mcp is not null)
{
    // The BFF calls the agent server-side (AGENT_BASE_URL is read per request, like BILLING_BASE_URL). Phase G, ADR-0017.
    web = web.WithEnvironment("AGENT_BASE_URL", mcp.GetEndpoint("http"))
        .WaitFor(mcp);
}

if (includeAuthservice)
{
    // Account routes need AUTH_BASE_URL and SESSION_SECRET (accountsConfigured in web/app/src/lib/bff-config.ts).
    // NODE_ENV=development keeps the session cookie without the Secure flag (secureCookies), so it is stored over
    // http://localhost; the `dev` script is the only way the web resource runs here.
    web = web.WithEnvironment("AUTH_BASE_URL", AuthBaseUrl)
        .WithEnvironment("NODE_ENV", "development");
    if (sessionSecret is not null)
    {
        web = web.WithEnvironment("SESSION_SECRET", sessionSecret);
    }
}

if (bffSharedSecret is not null)
{
    web = web.WithEnvironment("BFF_SHARED_SECRET", bffSharedSecret);
}

// Optional (P8): without a key the map page shows its list fallback. Read per request by the web app's runtime config.
if (googleMapsApiKey is not null)
{
    web = web.WithEnvironment("GOOGLE_MAPS_API_KEY", googleMapsApiKey);
}

// The demo seeder (src/SchoolSearch.Seeder): a one-shot console app. It creates the test accounts through authservice,
// then calls the dev seed endpoints of reviews and billing. It runs only when SeedDemo is on AND authservice is included:
// the test accounts live in authservice. An explicit IncludeAuthservice=false with SeedDemo is skipped with a banner
// line, not an error. The path overload is used because this AppHost does not reference the Seeder project. Nothing waits
// for the seeder, so the web app still browses when seeding fails. The seeder exits after its run.
var seederProject = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SchoolSearch.Seeder", "SchoolSearch.Seeder.csproj"));
string seederBanner;
if (!seedDemo)
{
    seederBanner = "  Seeder: off (SeedDemo is off).";
}
else if (!includeAuthservice)
{
    seederBanner = "  Seeder: skipped. SeedDemo is on but authservice is not included (IncludeAuthservice=false), so the test "
        + "accounts cannot be created. Unset IncludeAuthservice or set it to true to seed, or set SeedDemo=false.";
}
else if (authservice is null || billing is null || !File.Exists(seederProject))
{
    seederBanner = "  Seeder: skipped. A required project or resource is missing (authservice, billing or the Seeder project).";
}
else
{
    // authservice, billing and reviews are all non-null here. The seeder waits for them; its own probes wait for readiness.
    builder.AddProject("seeder", seederProject)
        .WithEnvironment("AUTH_BASE_URL", AuthBaseUrl)
        .WithEnvironment("REVIEWS_BASE_URL", reviews.GetEndpoint("http"))
        .WithEnvironment("BILLING_BASE_URL", billing.GetEndpoint("http"))
        .WithEnvironment("DEV_SEED_KEY", devSeedKey)
        .WithEnvironment("SEED_ADMIN_EMAIL", seedAdminEmail)
        .WithEnvironment("SEED_TEST_PASSWORD", seedTestPassword)
        .WithEnvironment("SEED_PASSWORD_GENERATED", seedPasswordSource == "configuration" ? "false" : "true")
        .WaitFor(reviews)
        .WaitFor(billing)
        .WaitFor(authservice);
    seederBanner = "  Seeder: on. It runs once after authservice, reviews and billing are ready; its resource log shows the summary.";
}

// Startup banner: what is enabled and where the credentials come from. It prints no secret value.
Console.WriteLine("SchoolSearch local run");
Console.WriteLine($"  SeedDemo (demo data and test accounts): {(seedDemo ? "on" : "off")}");
Console.WriteLine($"  Authservice container: {(includeAuthservice ? "on" : "off")}");
Console.WriteLine($"  AI (ANTHROPIC_API_KEY): {(anthropicApiKey is not null ? "on" : "off, AI features degrade")}");
Console.WriteLine($"  Google Maps (GOOGLE_MAPS_API_KEY): {(googleMapsApiKey is not null ? "on" : "off, the map page shows a list")}");
if (includeAuthservice)
{
    Console.WriteLine($"  Authservice JWT key: {DescribeKeySource(jwtKeySource, seedStoreProblem is null)}.");
    Console.WriteLine($"  Web sign-in (SESSION_SECRET): {DescribeSessionSecretSource(sessionSecretSource, seedStoreProblem is null)}.");
    if (sessionSecretWarning is not null)
    {
        Console.WriteLine($"  WARNING: {sessionSecretWarning}");
    }
}

if (seedDemo)
{
    Console.WriteLine(seederBanner);
    Console.WriteLine($"  Admin account: {seedAdminEmail}");
    Console.WriteLine(seedPasswordSource switch
    {
        "configuration" => "  Test password: user secret Seed:TestPassword (not printed).",
        "store" => "  Test password: stored in .local/seed-secrets.json; the seeder resource log in the Aspire dashboard shows it.",
        _ => seedStoreProblem is null
            ? "  Test password: generated and stored in .local/seed-secrets.json; the seeder resource log in the Aspire dashboard shows it."
            : "  Test password: generated for this run only; the seeder resource log in the Aspire dashboard shows it.",
    });
    if (seedStoreProblem is not null)
    {
        Console.WriteLine($"  WARNING: .local/seed-secrets.json was not updated: {seedStoreProblem}.");
        Console.WriteLine("  Values generated on this run last for this run only; set Seed:TestPassword in user-secrets to keep a stable login.");
    }

    Console.WriteLine("  To reset the local data, delete .local/seed-secrets.json AND the Docker volume school-search-postgres-data together;");
    Console.WriteLine("  deleting only one of them breaks login.");
}

builder.Build().Run();

static string DescribeSessionSecretSource(string source, bool saved) => source switch
{
    "configuration" => "configuration",
    "store" => "stored in .local/seed-secrets.json",
    "generated" => saved ? "generated and stored in .local/seed-secrets.json" : "generated for this run only (not stored)",
    "per run" => "generated for this run only (SeedDemo is off); sessions end when the AppHost restarts",
    _ => "not set, account routes answer 503",
};

static string DescribeKeySource(string source, bool saved) => source switch
{
    "configuration" => "user secret Authservice:JwtPrivateKeyPem",
    "store" => "stored in .local/seed-secrets.json",
    "generated" => saved ? "generated and stored in .local/seed-secrets.json" : "generated for this run only (not stored)",
    _ => "not set",
};
