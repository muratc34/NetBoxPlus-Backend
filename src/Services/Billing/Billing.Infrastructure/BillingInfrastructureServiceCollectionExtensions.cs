using Billing.Application.Abstractions;
using Billing.Domain.Customers;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Billing.Infrastructure.Idempotency;
using Billing.Infrastructure.Payments;
using Billing.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Messaging;
using Shared.Infrastructure.Persistence;

namespace Billing.Infrastructure;

public static class BillingInfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the database (from <c>ConnectionStrings:Database</c>) and RabbitMQ messaging (from the <c>Messaging</c> section).</summary>
    public static IServiceCollection AddBillingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(BillingDbContext.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{BillingDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

        services.AddBillingPersistence(connectionString);
        services.AddBillingPayments(configuration);
        services.AddHostedService<IdempotencyCleanupService>();
        services.AddRabbitMqMessaging();

        return services;
    }

    /// <summary>Stripe when a key is configured; otherwise a gateway that reports payments as unavailable.</summary>
    internal static IServiceCollection AddBillingPayments(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CheckoutOptions>().Bind(configuration.GetSection(CheckoutOptions.SectionName))
            .Validate(options => options.IsValid, $"{CheckoutOptions.SectionName}: SuccessUrl, CancelUrl and PortalReturnUrl must be absolute addresses.")
            .ValidateOnStart();

        var secretKey = configuration.GetSection(StripeOptions.SectionName).Get<StripeOptions>()?.SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            services.AddSingleton<IPaymentGateway, UnconfiguredPaymentGateway>();
        }
        else
        {
            services.AddSingleton<Stripe.IStripeClient>(new Stripe.StripeClient(secretKey));
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        }

        return services;
    }

    /// <summary>The database and repositories; the integration tests use it on its own.</summary>
    internal static IServiceCollection AddBillingPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPersistence<BillingDbContext>(connectionString);
        services.AddScoped<IBillingReadModel>(provider => provider.GetRequiredService<BillingDbContext>());
        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IdempotencyCleaner>();

        return services;
    }
}
