using Auth.Contracts;
using Billing.Application.Customers.CreateCustomer;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Billing.Application.Customers;

/// <summary>
/// Every account that registers becomes a billing customer. The full type name is this handler's
/// inbox key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class CreateCustomerOnUserRegistered(IDispatcher dispatcher) : IIntegrationEventHandler<UserRegistered>
{
    public Task<Result> Handle(UserRegistered integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new CreateCustomerCommand(integrationEvent.UserId, integrationEvent.Email), cancellationToken);
}
