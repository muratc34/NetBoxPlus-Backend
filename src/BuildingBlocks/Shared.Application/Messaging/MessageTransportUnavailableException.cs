namespace Shared.Application.Messaging;

/// <summary>
/// The broker cannot be reached at the moment. Nothing is wrong with the message: it is to be sent
/// again, however long the outage lasts, without counting as a failed attempt.
/// </summary>
public sealed class MessageTransportUnavailableException : Exception
{
    public MessageTransportUnavailableException()
    {
    }

    public MessageTransportUnavailableException(string message)
        : base(message)
    {
    }

    public MessageTransportUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
