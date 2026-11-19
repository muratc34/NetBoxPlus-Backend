using Auth.Contracts;
using Billing.Application.Customers.ChangeCustomerEmail;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Billing.Application.Customers;

/// <summary>
/// Keeps the customer's address, and the payment provider's, the user's. The full type name is this
/// handler's inbox key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class ChangeCustomerEmailOnEmailChanged(IDispatcher dispatcher) : IIntegrationEventHandler<EmailChanged>
{
    public Task<Result> Handle(EmailChanged integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new ChangeCustomerEmailCommand(integrationEvent.UserId, integrationEvent.Email, integrationEvent.Revision), cancellationToken);
}
