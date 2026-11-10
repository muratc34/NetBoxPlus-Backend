namespace Billing.Domain.Customers;

public interface ICustomerRepository
{
    Task<bool> ExistsForUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<BillingCustomer?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    void Add(BillingCustomer customer);
}
