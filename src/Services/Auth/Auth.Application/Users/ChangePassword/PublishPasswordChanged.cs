using Auth.Contracts;
using Auth.Domain.Users;
using Shared.Application.Events;

namespace Auth.Application.Users.ChangePassword;

/// <summary>
/// Tells the notification service, which e-mails the user a notice. Runs while the new password is
/// being saved, so a change that failed is never announced.
/// </summary>
internal sealed class PublishPasswordChanged(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<PasswordChangedDomainEvent>
{
    public Task Handle(PasswordChangedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new PasswordChanged(domainEvent.UserId, domainEvent.ChangeId, domainEvent.ChangedAt, timeProvider.GetUtcNow()),
            cancellationToken);
}
