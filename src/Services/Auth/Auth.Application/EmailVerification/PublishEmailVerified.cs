using Auth.Contracts;
using Auth.Domain.Users;
using Shared.Application.Events;

namespace Auth.Application.EmailVerification;

/// <summary>
/// Tells billing, which lets only verified accounts start a subscription. Runs while the
/// verification is being saved, so one that failed is never announced.
/// </summary>
internal sealed class PublishEmailVerified(IIntegrationEventPublisher publisher, TimeProvider timeProvider)
    : IDomainEventHandler<EmailVerifiedDomainEvent>
{
    public Task Handle(EmailVerifiedDomainEvent domainEvent, CancellationToken cancellationToken)
        => publisher.PublishAsync(
            new EmailVerified(domainEvent.UserId, domainEvent.Email, domainEvent.VerifiedAt, timeProvider.GetUtcNow()),
            cancellationToken);
}
