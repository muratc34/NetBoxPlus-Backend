using System.Diagnostics;
using System.Text.Json;
using Shared.Application.Events;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Adds the event to the outbox of the current context. Nothing is written until the unit of work
/// saves, so the event exists exactly when the change that published it does.
/// </summary>
internal sealed class OutboxIntegrationEventPublisher<TContext>(TContext context, TimeProvider timeProvider)
    : IIntegrationEventPublisher
    where TContext : ServiceDbContext
{
    public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        // The runtime type, not TEvent: a caller holding the event as a base type still publishes the concrete contract.
        var eventType = integrationEvent.GetType();

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = IntegrationEvents.MessageTypeOf(eventType),
            Payload = JsonSerializer.Serialize(integrationEvent, eventType, IntegrationEvents.SerializerOptions),
            OccurredAt = timeProvider.GetUtcNow(),
            TraceParent = Activity.Current?.Id,
        });

        return Task.CompletedTask;
    }
}
