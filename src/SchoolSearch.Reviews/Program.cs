using SchoolSearch.Reviews.Ai;
using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.DevSeed;
using SchoolSearch.Reviews.Dsa;
using SchoolSearch.Reviews.Endpoints;
using SchoolSearch.Reviews.Extensions;
using SchoolSearch.ServiceDefaults;

// Program.cs is a manifest (P9): each line is one capability, wired in its own extension method.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDatabaseContext<ReviewsDbContext>(builder.Configuration, "reviewsdb", "ReviewsInMemory");
builder.Services.AddAuthServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddReviewsServices(builder.Configuration);
builder.Services.AddDsaServices(builder.Configuration);
builder.Services.AddReviewTriage(builder.Configuration);
builder.Services.AddDevSeed(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseReviewsPipeline();
app.MapReviewEndpoints();
app.MapDsaEndpoints();
app.MapDevSeedEndpoints();

app.Run();

// Public so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
