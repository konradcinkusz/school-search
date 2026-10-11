using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SchoolSearch.Billing.Errors;
using SchoolSearch.Billing.Models;
using SchoolSearch.Billing.Settings;

namespace SchoolSearch.Billing.Plans;

/// <summary>Public plan list. Anonymous. Prices are null until published; no price or tax rule is invented here.</summary>
public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/plans", ListPlans);
        return app;
    }

    internal static IResult ListPlans(PlanCatalog catalog, BillingMode mode)
    {
        var items = catalog.Plans
            .Select(plan => new PlanItem(plan.Code, plan.Name, plan.PriceGrossPln, plan.Interval))
            .ToList();

        var note = items.Count == 0
            ? PremiumCopy.PlansInPreparation
            : mode.IsMock ? PremiumCopy.MockSalesNote : null;

        return Results.Json(
            new PlansResponse(items, note, mode.IsMock ? "mock" : "live"),
            ApiJson.Options);
    }
}
