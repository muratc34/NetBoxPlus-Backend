using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Billing.Application;

public static class BillingApplicationServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher and every handler and validator of the billing service.</summary>
    public static IServiceCollection AddBillingApplication(this IServiceCollection services)
    {
        services.AddCqrs(typeof(BillingApplicationServiceCollectionExtensions).Assembly);
        return services;
    }
}
