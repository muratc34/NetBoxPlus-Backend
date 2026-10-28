using Shared.Domain;

namespace Shared.Application.Events;

/// <summary>
/// Sends domain events to their handlers. Used by the persistence layer while saving; application
/// code raises events on aggregates instead of calling this.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
