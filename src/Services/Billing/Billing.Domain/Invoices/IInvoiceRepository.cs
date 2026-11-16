namespace Billing.Domain.Invoices;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByProviderIdAsync(string providerInvoiceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Invoice>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Invoice invoice);
}
