namespace Shared.Domain;

/// <summary>
/// Marker for something that happened inside an aggregate. Domain events are handled in-process,
/// within the same transaction; events that leave the service are integration events instead.
/// </summary>
public interface IDomainEvent;
