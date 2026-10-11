// Composition root for local development (P1). This is NOT the production topology:
// production is described by flyio/*.fly.toml and the tag-driven workflow.
// No secret literal lives here. Aspire generates a throwaway password for the local Postgres
// container, so a fresh clone needs zero credentials (P8).
using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

// authservice is the external identity provider (ADR-0004, ADR-0015), run here as a container. It is OFF by default,
// so the zero-credential run is unchanged. Switch it on with IncludeAuthservice=true (environment or user-secrets).
// It needs a user secret Authservice:JwtPrivateKeyPem (RS256 PEM, one line with literal \n escapes): without it
// authservice falls back to HS256, its JWKS stays empty, and no consumer can validate a token.
var includeAuthservice = bool.TryParse(builder.Configuration["IncludeAuthservice"], out var include) && include;

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

if (includeAuthservice)
{
    var authDb = postgres.AddDatabase("authservicedb");

    var jwtKey = builder.Configuration["Authservice:JwtPrivateKeyPem"]
        ?? throw new InvalidOperationException(
            "IncludeAuthservice=true needs the user secret Authservice:JwtPrivateKeyPem (RS256 PEM). See docs/runbooks/AUTHSERVICE.md section 5.");

    // Pinned tag, never :latest (flyio/authservice.fly.toml uses the same image). Dev only: production runs on Fly.
    var authservice = builder.AddContainer("authservice", "ghcr.io/konradcinkusz/authservice", "v0.3.4")
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

if (includeAuthservice)
{
    web = web.WithEnvironment("AUTH_BASE_URL", AuthBaseUrl);
}

builder.Build().Run();
