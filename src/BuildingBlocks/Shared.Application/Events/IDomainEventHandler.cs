using Shared.Domain;

namespace Shared.Application.Events;

/// <summary>
/// Reacts to a domain event inside the same transaction as the change that raised it. A handler
/// that throws fails the whole save.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
