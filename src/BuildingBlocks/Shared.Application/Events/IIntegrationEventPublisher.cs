namespace Shared.Application.Events;

/// <summary>
/// Publishes an event to other services. The event is written to the outbox as part of the current
/// unit of work and sent to the broker only after the save commits — never directly.
/// </summary>
/// <remarks>
/// Integration events are plain records defined in the publishing service's <c>*.Contracts</c>
/// project. They implement no marker interface so that contracts stay free of dependencies.
/// </remarks>
public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class;
}
