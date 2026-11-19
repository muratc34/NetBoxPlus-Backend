using Auth.Contracts;
using Auth.Domain.Users;
using Shared.Application.Events;

namespace Auth.Application.Users.Register;

/// <summary>
/// Tells other services about a new account. Runs while the user is being saved, so the outbox
/// message is committed in the same transaction as the account itself.
/// </summary>
internal sealed class PublishUserRegistered(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<UserRegisteredDomainEvent>
{
    public Task Handle(UserRegisteredDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new UserRegistered(domainEvent.UserId, domainEvent.Email, timeProvider.GetUtcNow()),
            cancellationToken);
}
