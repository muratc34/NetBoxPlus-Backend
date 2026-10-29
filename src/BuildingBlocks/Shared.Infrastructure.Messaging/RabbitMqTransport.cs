using System.Text;
using RabbitMQ.Client;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Publishes envelopes with publisher confirms: <see cref="SendAsync"/> returns only after the
/// broker accepted the message, so the outbox marks a message processed only when it is safe.
/// The envelope maps onto AMQP properties; the body is the payload JSON alone.
/// </summary>
internal sealed class RabbitMqTransport(RabbitMqConnection connection) : IMessageTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _declaredExchanges = new(StringComparer.Ordinal);
    private IChannel? _channel;

    public async Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var exchange = RabbitMqTopology.ExchangeFor(envelope.MessageType);
        using var activity = MessagingTelemetry.StartPublish(envelope, exchange);

        var properties = new BasicProperties
        {
            MessageId = envelope.MessageId.ToString(),
            Type = envelope.MessageType,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(envelope.OccurredAt.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(),
        };

        if ((activity?.Id ?? envelope.TraceParent) is { } traceParent)
        {
            properties.Headers[MessagingTelemetry.TraceParentHeader] = traceParent;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);

            if (!_declaredExchanges.Contains(exchange))
            {
                await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _declaredExchanges.Add(exchange);
            }

            await channel.BasicPublishAsync(
                exchange,
                routingKey: envelope.MessageType,
                mandatory: false,
                basicProperties: properties,
                body: Encoding.UTF8.GetBytes(envelope.Payload),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } open)
        {
            return open;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        var brokerConnection = await connection.GetAsync(cancellationToken).ConfigureAwait(false);
        _channel = await brokerConnection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);
        _declaredExchanges.Clear();

        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }

        _lock.Dispose();
    }
}
