namespace Shared.Application.Messaging;

/// <summary>
/// An integration event on its way between services, independent of the broker. The outbox stores
/// it, the transport sends it, the consumer reads it back.
/// </summary>
/// <param name="MessageId">Unique per message; consumers use it to process each message once.</param>
/// <param name="MessageType">The contract type's full name, for example <c>Billing.Contracts.SubscriptionCreated</c>.</param>
/// <param name="OccurredAt">When the event was published.</param>
/// <param name="TraceParent">W3C trace context of the request that published it, so the trace continues across the broker.</param>
/// <param name="Payload">The event serialized as JSON.</param>
public sealed record MessageEnvelope(
    Guid MessageId,
    string MessageType,
    DateTimeOffset OccurredAt,
    string? TraceParent,
    string Payload);
