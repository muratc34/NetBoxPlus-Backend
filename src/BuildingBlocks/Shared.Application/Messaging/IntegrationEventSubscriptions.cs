namespace Shared.Application.Messaging;

/// <summary>
/// The integration event types this service handles, collected by <c>AddCqrs</c> while scanning.
/// The messaging layer subscribes to exactly these.
/// </summary>
public sealed class IntegrationEventSubscriptions
{
    private readonly HashSet<Type> _eventTypes = [];

    public IReadOnlyCollection<Type> EventTypes => _eventTypes;

    internal void Add(Type eventType) => _eventTypes.Add(eventType);
}
