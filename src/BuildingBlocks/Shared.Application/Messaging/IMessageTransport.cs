namespace Shared.Application.Messaging;

/// <summary>
/// Hands a message to the broker. Implemented by the messaging layer and called by the outbox
/// processor, so persistence never depends on a broker library.
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// Returns once the broker has confirmed the message. Throws <see cref="MessageTransportUnavailableException"/>
    /// when the broker cannot be reached, and any other exception when the message itself was not accepted.
    /// </summary>
    Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}
