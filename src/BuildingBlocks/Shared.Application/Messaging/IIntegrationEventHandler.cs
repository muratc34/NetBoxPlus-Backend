using Shared.Domain;

namespace Shared.Application.Messaging;

/// <summary>
/// Reacts to an event published by another service. Each message reaches a handler at most once;
/// like command handlers, integration event handlers do not call SaveChanges.
/// </summary>
/// <remarks>
/// Return a failed result for an outcome that will not change on retry (the message is then
/// recorded as processed); throw for a transient problem (the message is retried, then
/// dead-lettered).
/// </remarks>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task<Result> Handle(TEvent integrationEvent, CancellationToken cancellationToken);
}
