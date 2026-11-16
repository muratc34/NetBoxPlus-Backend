using Auth.Contracts;
using Auth.Domain.Users;
using Shared.Application.Events;

namespace Auth.Application.EmailChange;

/// <summary>
/// Tells the services that copy the address (billing, notification). Runs while the change is being
/// saved, so a change that failed is never announced.
/// </summary>
internal sealed class PublishEmailChanged(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<EmailChangedDomainEvent>
{
    public Task Handle(EmailChangedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new EmailChanged(domainEvent.UserId, domainEvent.Email, domainEvent.Revision, domainEvent.ChangedAt, timeProvider.GetUtcNow()),
            cancellationToken);
}
