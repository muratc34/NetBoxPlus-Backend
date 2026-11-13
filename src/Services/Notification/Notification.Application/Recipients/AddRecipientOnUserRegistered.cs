using Auth.Contracts;
using Notification.Application.Recipients.AddRecipient;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.Application.Recipients;

/// <summary>
/// Every account that registers can be written to. The full type name is this handler's inbox key:
/// renaming or moving it would process the messages it already handled again.
/// </summary>
internal sealed class AddRecipientOnUserRegistered(IDispatcher dispatcher) : IIntegrationEventHandler<UserRegistered>
{
    public Task<Result> Handle(UserRegistered integrationEvent, CancellationToken cancellationToken)
        => dispatcher.Send(new AddRecipientCommand(integrationEvent.UserId, integrationEvent.Email), cancellationToken);
}
