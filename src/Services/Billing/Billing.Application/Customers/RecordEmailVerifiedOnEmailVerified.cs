using Auth.Contracts;
using Billing.Application.Customers.RecordEmailVerified;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Billing.Application.Customers;

/// <summary>
/// Only a verified account may start a subscription. The full type name is this handler's inbox
/// key: renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class RecordEmailVerifiedOnEmailVerified(IDispatcher dispatcher) : IIntegrationEventHandler<EmailVerified>
{
    public Task<Result> Handle(EmailVerified integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new RecordEmailVerifiedCommand(integrationEvent.UserId, integrationEvent.Email, integrationEvent.VerifiedAt), cancellationToken);
}
