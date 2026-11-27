using Auth.Contracts;
using Auth.Domain.Users;
using Shared.Application.Events;

namespace Auth.Application.Sessions;

/// <summary>Run while the sign-in is being saved: the outbox message commits with the new session.</summary>
internal sealed class PublishSignedIn(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<SignedInDomainEvent>
{
    public Task Handle(SignedInDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new SignedIn(
                domainEvent.UserId,
                domainEvent.SessionId,
                domainEvent.DeviceLabel,
                domainEvent.Kind,
                domainEvent.SignedInAt,
                domainEvent.Registration,
                timeProvider.GetUtcNow()),
            cancellationToken);
}
