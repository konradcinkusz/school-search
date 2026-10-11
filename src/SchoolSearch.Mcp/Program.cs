using SchoolSearch.Mcp.Catalog;
using SchoolSearch.Mcp.Options;
using SchoolSearch.Mcp.Prompts;
using SchoolSearch.Mcp.RateLimiting;
using SchoolSearch.Mcp.Resources;
using SchoolSearch.Mcp.Reviews;
using SchoolSearch.Mcp.Security;
using SchoolSearch.Mcp.Tools;
using SchoolSearch.ServiceDefaults;

// Program.cs is a manifest: one line per capability. The MCP server is read-only and owns no database.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection(McpOptions.SectionName));
builder.Services.AddCatalogClient(builder.Configuration);
builder.Services.AddReviewsClient(builder.Configuration);
builder.Services.AddMcpRateLimiting();

// Streamable HTTP transport. Anonymous in v1; the MCP server authenticates to the catalog with its own key.
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true) // read-only tools: no per-session state, no machine affinity
    .WithTools<SchoolTools>()
    .WithTools<PreschoolTools>()
    .WithTools<ReviewsTools>()
    .WithResources<SchoolResources>()
    .WithPrompts<SchoolPrompts>();

var app = builder.Build();

app.UseMcpSecurityHeaders();
app.UseRateLimiter();
app.MapDefaultEndpoints();
app.MapMcp("/mcp");

app.Run();

// Public so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
