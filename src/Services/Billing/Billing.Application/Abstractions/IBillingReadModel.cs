using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Billing.Domain.Plans;
using Billing.Domain.Subscriptions;

namespace Billing.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface IBillingReadModel
{
    IQueryable<Plan> Plans { get; }

    IQueryable<BillingCustomer> Customers { get; }

    IQueryable<Subscription> Subscriptions { get; }

    IQueryable<Invoice> Invoices { get; }
}
