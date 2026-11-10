using Billing.Domain.Customers;
using Billing.Domain.Plans;

namespace Billing.Application.Abstractions;

/// <summary>Untracked queryable views for query handlers, which project straight to response types.</summary>
public interface IBillingReadModel
{
    IQueryable<Plan> Plans { get; }

    IQueryable<BillingCustomer> Customers { get; }
}
