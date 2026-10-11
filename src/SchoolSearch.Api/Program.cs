using SchoolSearch.Api.Extensions;
using SchoolSearch.Catalog.Data;
using SchoolSearch.ServiceDefaults;

// Program.cs is a manifest (P9): each line is one capability, wired in its own extension method.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddCorsPolicy(builder.Configuration, CorsPolicies.Frontend);
builder.Services.AddDatabaseContext<CatalogDbContext>(builder.Configuration, "catalogdb", "CatalogInMemory");
builder.Services.AddCatalogApi(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCatalogApi();
app.MapCatalogEndpoints();

app.Run();

// Public so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
