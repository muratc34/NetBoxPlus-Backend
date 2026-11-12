using Auth.Contracts;
using Auth.Domain.Users;
using Shared.Application.Events;

namespace Auth.Application.Profiles;

// Run while the change is being saved: the outbox message commits with the change itself.

internal sealed class PublishProfileCreated(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<ProfileCreatedDomainEvent>
{
    public Task Handle(ProfileCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new ProfileCreated(domainEvent.UserId, domainEvent.ProfileId, domainEvent.MaxAgeRating.ToString(), domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}

internal sealed class PublishProfileChanged(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<ProfileChangedDomainEvent>
{
    public Task Handle(ProfileChangedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new ProfileChanged(domainEvent.UserId, domainEvent.ProfileId, domainEvent.MaxAgeRating.ToString(), domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}

internal sealed class PublishProfileRemoved(IIntegrationEventPublisher publisher, TimeProvider timeProvider) : IDomainEventHandler<ProfileRemovedDomainEvent>
{
    public Task Handle(ProfileRemovedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new ProfileRemoved(domainEvent.UserId, domainEvent.ProfileId, domainEvent.Revision, timeProvider.GetUtcNow()),
            cancellationToken);
}
