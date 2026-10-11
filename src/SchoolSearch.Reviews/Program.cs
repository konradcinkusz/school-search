using SchoolSearch.Reviews.Auth;
using SchoolSearch.Reviews.Data;
using SchoolSearch.Reviews.Endpoints;
using SchoolSearch.Reviews.Extensions;
using SchoolSearch.ServiceDefaults;

// Program.cs is a manifest (P9): each line is one capability, wired in its own extension method.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDatabaseContext<ReviewsDbContext>(builder.Configuration, "reviewsdb", "ReviewsInMemory");
builder.Services.AddAuthServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddReviewsServices(builder.Configuration);
// WIRING-DSA: builder.Services.AddDsaServices(builder.Configuration);
// WIRING-AI: builder.Services.AddReviewTriage(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseReviewsPipeline();
app.MapReviewEndpoints();
// WIRING-DSA: app.MapDsaEndpoints();

app.Run();

// Public so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
