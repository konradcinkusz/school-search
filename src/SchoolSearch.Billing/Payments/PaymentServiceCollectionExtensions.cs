using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SchoolSearch.Billing.Payments;

/// <summary>Registers the webhook, simulator and event-mapping machinery. Called from Program.cs (WIRING-PAYMENTS).</summary>
public static class PaymentServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new PaymentSettings(configuration, sp.GetRequiredService<IHostEnvironment>()));

        // The provider adapter is replaceable here. GenericJsonEventMapper is the only shipped placeholder.
        services.AddSingleton<IBillingEventMapper, GenericJsonEventMapper>();

        services.AddScoped<BillingWebhookHandler>();
        return services;
    }
}
