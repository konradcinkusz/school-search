using SchoolSearch.Billing.Auth;
using SchoolSearch.Billing.Ai;
using SchoolSearch.Billing.Data;
using SchoolSearch.Billing.Extensions;
using SchoolSearch.Billing.Leads;
using SchoolSearch.Billing.Payments;
using SchoolSearch.ServiceDefaults;

// Program.cs is a manifest (P9): each line is one capability, wired in its own extension method.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddDatabaseContext<BillingDbContext>(builder.Configuration, "billingdb", "BillingInMemory");
builder.Services.AddAuthServiceAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddBillingServices(builder.Configuration, builder.Environment);

// WIRING-PAYMENTS
builder.Services.AddPaymentServices(builder.Configuration);
// WIRING-LEADS
builder.Services.AddLeadServices(builder.Configuration);
// WIRING-AI
builder.Services.AddProfileDraftAssistant(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseBillingPipeline();
app.MapBillingEndpoints();
// WIRING-PAYMENTS
app.MapPaymentEndpoints();
// WIRING-LEADS
app.MapLeadEndpoints();
// WIRING-AI
app.MapProfileDraftEndpoints();

app.Run();

// Public so the test project can host the app with WebApplicationFactory<Program>.
public partial class Program { }
