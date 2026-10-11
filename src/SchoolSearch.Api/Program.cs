using SchoolSearch.Api.Ai;
using SchoolSearch.Api.Catchment;
using SchoolSearch.Api.Corrections;
using SchoolSearch.Api.Demo;
using SchoolSearch.Api.Endpoints;
using SchoolSearch.Api.Extensions;
using SchoolSearch.Api.Preschools;
using SchoolSearch.Api.Search;
using SchoolSearch.Api.Security;
using SchoolSearch.Catalog.Data;
using SchoolSearch.ServiceDefaults;

// Program.cs is a manifest (P9): each line is one capability, wired in its own extension method.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCorsPolicy(builder.Configuration, CorsPolicies.Frontend);
builder.Services.AddDatabaseContext<CatalogDbContext>(builder.Configuration, "catalogdb", "CatalogInMemory");
builder.Services.AddCatalogApi(builder.Configuration);
builder.Services.AddSearchServices();
builder.Services.AddCorrectionServices();
builder.Services.AddSecurityServices(builder.Configuration);
builder.Services.AddAuthServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddDemoSeed(builder.Configuration);
builder.Services.AddAiServices(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCatalogApi();
app.MapCatalogEndpoints();
app.MapSearchEndpoints();
app.MapPreschoolEndpoints();
app.MapCatchmentEndpoints();
app.MapCorrectionEndpoints();
app.MapKeyEndpoints();
app.MapAdminEndpoints();
app.MapAiEndpoints();

app.Run();

// Public so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
