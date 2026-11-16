using Billing.Application.Abstractions;
using Billing.Application.Customers.ChangeCustomerEmail;
using Billing.Domain.Customers;
using Billing.Domain.Invoices;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Billing.Application.Customers.DeleteCustomer;

/// <summary>
/// The user's account was deleted: the payment provider's customer is deleted, which cancels its
/// subscription at once without a refund (the provider then reports the cancellation like any other),
/// and billing's own copy is anonymized. Invoices stay, as financial records, without the card's
/// details. When billing has not heard of the user yet, an anonymized customer is kept so that a
/// late registration does not bring the address back. Coming again changes nothing.
/// </summary>
public sealed record DeleteCustomerCommand(Guid UserId, DateTimeOffset DeletedAt) : ICommand;

internal sealed class DeleteCustomerHandler(
    ICustomerRepository customers,
    IInvoiceRepository invoices,
    IPaymentGateway payments) : ICommandHandler<DeleteCustomerCommand>
{
    public async Task<Result> Handle(DeleteCustomerCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (customer is null)
        {
            // Racing the registration event here is settled by the unique index: the loser retries.
            customer = BillingCustomer.Create(command.UserId, BillingCustomer.DeletedEmail(command.UserId));
            customers.Add(customer);
        }

        if (customer.DeletedAt is null && customer.ProviderCustomerId is { } providerCustomerId)
        {
            var deleted = await payments.DeleteCustomerAsync(providerCustomerId, cancellationToken).ConfigureAwait(false);
            if (deleted.IsFailure && deleted.Error.Type == ErrorType.Unavailable)
            {
                // Not saved: the message is tried again, and the provider's customer is deleted then.
                throw new PaymentProviderUnavailableException("The payment provider could not be reached to delete the customer.");
            }

            if (deleted.IsFailure)
            {
                // A refusal is not something a retry changes: the message is recorded, the copy stays as it is.
                return deleted.Error;
            }
        }

        customer.Delete(command.DeletedAt);
        foreach (var invoice in await invoices.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false))
        {
            invoice.ForgetCard();
        }

        return Result.Success();
    }
}
