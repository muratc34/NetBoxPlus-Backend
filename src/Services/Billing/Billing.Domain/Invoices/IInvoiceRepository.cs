namespace Billing.Domain.Invoices;

public interface IInvoiceRepository
{
    Task<Invoice?> GetByProviderIdAsync(string providerInvoiceId, CancellationToken cancellationToken);

    void Add(Invoice invoice);
}
