using Auth.Contracts;
using Auth.Domain.Passkeys;
using Shared.Application.Events;

namespace Auth.Application.Passkeys;

/// <summary>
/// Tells the notification service a passkey was added, so the user hears of a way in they may not have made. Runs
/// while the passkey is being saved, so one that failed is never announced.
/// </summary>
internal sealed class PublishPasskeyAdded(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<PasskeyAddedDomainEvent>
{
    public Task Handle(PasskeyAddedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new PasskeyAdded(domainEvent.UserId, domainEvent.PasskeyId, domainEvent.Name, domainEvent.AddedAt, timeProvider.GetUtcNow()),
            cancellationToken);
}

/// <summary>Tells the notification service a passkey was removed.</summary>
internal sealed class PublishPasskeyRemoved(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<PasskeyRemovedDomainEvent>
{
    public Task Handle(PasskeyRemovedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new PasskeyRemoved(domainEvent.UserId, domainEvent.PasskeyId, domainEvent.Name, domainEvent.RemovedAt, timeProvider.GetUtcNow()),
            cancellationToken);
}
