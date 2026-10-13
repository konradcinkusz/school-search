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

// Demo seed values. The test password is the user secret Seed:TestPassword when set (reused, not printed); otherwise a
// random one is generated per run and the seeder prints it once. Empty or unused when SeedDemo is off.
var seedAdminEmail = LocalSeedSupport.OrDefault(builder.Configuration["Seed:AdminEmail"], "admin@school-search.test");
var configuredTestPassword = LocalSeedSupport.NonBlank(builder.Configuration["Seed:TestPassword"]);
var seedPasswordGenerated = seedDemo && configuredTestPassword is null;
var seedTestPassword = !seedDemo ? string.Empty : configuredTestPassword ?? LocalSeedSupport.CreatePassword();
var devSeedKey = seedDemo ? LocalSeedSupport.CreateDevSeedKey() : string.Empty;

// The RS256 key of authservice, when it is set as a user secret. Otherwise see the authservice block below.
var configuredJwtKey = LocalSeedSupport.NonBlank(builder.Configuration["Authservice:JwtPrivateKeyPem"]);

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

// The Reviews service owns reviewsdb (database per service). Its subject pepper keys the author hashes. Set
// Reviews:SubjectPepper in user-secrets to keep hashes stable across runs; otherwise a random pepper is generated
// per run, which is acceptable for local data only.
var subjectPepper = builder.Configuration["Reviews:SubjectPepper"]
    ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

var reviews = builder.AddProject<Projects.SchoolSearch_Reviews>("reviews")
    .WithEnvironment("Reviews__SubjectPepper", subjectPepper)
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

    // Keys the subject hash of claims and subscriptions. A per-run random pepper is acceptable for local data only.
    var billingPepper = builder.Configuration["Billing:SubjectPepper"]
        ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

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

    // With SeedDemo and no user secret, an ephemeral RS256 key is generated for this run only.
    var jwtKey = configuredJwtKey
        ?? (seedDemo ? LocalSeedSupport.CreateJwtPrivateKeyPem() : null)
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
        .WithEnvironment("Jwt__PrivateKeyPem", jwtKey)
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
    web = web.WithEnvironment("AUTH_BASE_URL", AuthBaseUrl);
}

// The demo seeder (src/SchoolSearch.Seeder): a one-shot console app. It creates the test accounts through authservice,
// then calls the dev seed endpoints of reviews and billing. It is added only once its project exists (the same rule as
// Billing and MCP). The path overload is used because this AppHost does not reference the Seeder project. Nothing waits
// for the seeder, so the web app still browses when seeding fails. The seeder exits after its run.
var seederProject = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "SchoolSearch.Seeder", "SchoolSearch.Seeder.csproj"));
if (seedDemo && File.Exists(seederProject))
{
    var seeder = builder.AddProject("seeder", seederProject)
        .WithEnvironment("AUTH_BASE_URL", AuthBaseUrl)
        .WithEnvironment("REVIEWS_BASE_URL", reviews.GetEndpoint("http"))
        .WithEnvironment("DEV_SEED_KEY", devSeedKey)
        .WithEnvironment("SEED_ADMIN_EMAIL", seedAdminEmail)
        .WithEnvironment("SEED_TEST_PASSWORD", seedTestPassword)
        .WithEnvironment("SEED_PASSWORD_GENERATED", seedPasswordGenerated ? "true" : "false")
        .WaitFor(reviews);

    if (billing is not null)
    {
        seeder = seeder
            .WithEnvironment("BILLING_BASE_URL", billing.GetEndpoint("http"))
            .WaitFor(billing);
    }

    if (authservice is not null)
    {
        seeder = seeder.WaitFor(authservice);
    }
}

// Startup banner: what is enabled and where the credentials come from. It prints no secret value.
Console.WriteLine("SchoolSearch local run");
Console.WriteLine($"  SeedDemo (demo data and test accounts): {(seedDemo ? "on" : "off")}");
Console.WriteLine($"  Authservice container: {(includeAuthservice ? "on" : "off")}");
Console.WriteLine($"  AI (ANTHROPIC_API_KEY): {(anthropicApiKey is not null ? "on" : "off, AI features degrade")}");
if (includeAuthservice)
{
    Console.WriteLine(configuredJwtKey is not null
        ? "  Authservice JWT key: user secret Authservice:JwtPrivateKeyPem."
        : "  Authservice JWT key: generated for this run, never written to disk.");
}

if (seedDemo)
{
    Console.WriteLine($"  Admin account: {seedAdminEmail}");
    Console.WriteLine(seedPasswordGenerated
        ? "  Test password: generated for this run; see the seeder resource log in the Aspire dashboard for the generated test password."
        : "  Test password: user secret Seed:TestPassword (not printed).");
}

builder.Build().Run();
