using Auth.Contracts;
using Billing.Application.Customers.DeleteCustomer;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Billing.Application.Customers;

/// <summary>
/// Deletes what billing keeps of a deleted user. The full type name is this handler's inbox key:
/// renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class DeleteCustomerOnUserDeleted(IDispatcher dispatcher) : IIntegrationEventHandler<UserDeleted>
{
    public Task<Result> Handle(UserDeleted integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new DeleteCustomerCommand(integrationEvent.UserId, integrationEvent.DeletedAt), cancellationToken);
}
