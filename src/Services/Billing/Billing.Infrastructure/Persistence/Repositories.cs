using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure.Persistence;

internal sealed class PlanRepository(BillingDbContext context) : IPlanRepository
{
    public Task<Plan?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        => context.Plans.FirstOrDefaultAsync(plan => plan.Id == id, cancellationToken);

    public Task<bool> IsProviderPriceTakenAsync(Plan plan, CancellationToken cancellationToken)
        => context.Plans.AnyAsync(other => other.Id != plan.Id && other.ProviderPriceId == plan.ProviderPriceId, cancellationToken);

    public Task<Plan?> GetByProviderPriceIdAsync(string providerPriceId, CancellationToken cancellationToken)
        => context.Plans.FirstOrDefaultAsync(plan => plan.ProviderPriceId == providerPriceId, cancellationToken);

    public void Add(Plan plan) => context.Plans.Add(plan);
}

internal sealed class CustomerRepository(BillingDbContext context) : ICustomerRepository
{
    public Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken)
        => context.Customers.AnyAsync(customer => customer.UserId == userId, cancellationToken);

    public Task<BillingCustomer?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        => context.Customers.FirstOrDefaultAsync(customer => customer.UserId == userId, cancellationToken);

    public Task<BillingCustomer?> GetByProviderCustomerIdAsync(string providerCustomerId, CancellationToken cancellationToken)
        => context.Customers.FirstOrDefaultAsync(customer => customer.ProviderCustomerId == providerCustomerId, cancellationToken);

    public void Add(BillingCustomer customer) => context.Customers.Add(customer);
}

internal sealed class SubscriptionRepository(BillingDbContext context) : ISubscriptionRepository
{
    public Task<bool> HasLiveAsync(Guid userId, CancellationToken cancellationToken)
        => context.Subscriptions.AnyAsync(subscription => subscription.UserId == userId && subscription.Status != SubscriptionStatus.Canceled, cancellationToken);

    public Task<Subscription?> GetByProviderIdAsync(string providerSubscriptionId, CancellationToken cancellationToken)
        => context.Subscriptions.FirstOrDefaultAsync(subscription => subscription.ProviderSubscriptionId == providerSubscriptionId, cancellationToken);

    public void Add(Subscription subscription) => context.Subscriptions.Add(subscription);
}

internal sealed class InvoiceRepository(BillingDbContext context) : IInvoiceRepository
{
    public Task<Invoice?> GetByProviderIdAsync(string providerInvoiceId, CancellationToken cancellationToken)
        => context.Invoices.FirstOrDefaultAsync(invoice => invoice.ProviderInvoiceId == providerInvoiceId, cancellationToken);

    public void Add(Invoice invoice) => context.Invoices.Add(invoice);
}
