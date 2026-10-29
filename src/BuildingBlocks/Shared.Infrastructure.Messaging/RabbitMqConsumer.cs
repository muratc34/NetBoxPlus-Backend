using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Consumes the service's queue. Declares the topology (queue, bindings for every handled event
/// type, dead-letter queue), then hands each delivery to its handlers. A handler that throws is
/// retried in-process after the configured delays; after the last one the message is
/// dead-lettered. Messages that cannot be read at all are dead-lettered at once.
/// </summary>
internal sealed partial class RabbitMqConsumer(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<RabbitMqConsumer> logger,
    IntegrationEventSubscriptions? subscriptions = null)
    : BackgroundService
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var invokers = (subscriptions?.EventTypes ?? [])
            .ToDictionary(IntegrationEvents.MessageTypeOf, IntegrationEventInvoker.For, StringComparer.Ordinal);

        if (invokers.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(invokers, stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogConsumerFailed(logger, exception, ReconnectDelay);
                try
                {
                    await Task.Delay(ReconnectDelay, timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ConsumeAsync(Dictionary<string, IntegrationEventInvoker> invokers, CancellationToken cancellationToken)
    {
        var brokerConnection = await connection.GetAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await brokerConnection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        var queue = RabbitMqTopology.QueueFor(_options.ServiceName);
        await DeclareTopologyAsync(channel, queue, invokers.Keys, cancellationToken).ConfigureAwait(false);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: _options.PrefetchCount, global: false, cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, queue, delivery, invokers, cancellationToken);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken).ConfigureAwait(false);

        LogConsuming(logger, queue, invokers.Count);
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    private async Task DeclareTopologyAsync(IChannel channel, string queue, IEnumerable<string> messageTypes, CancellationToken cancellationToken)
    {
        var deadLetterQueue = RabbitMqTopology.DeadLetterQueueFor(_options.ServiceName);

        await channel.ExchangeDeclareAsync(RabbitMqTopology.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await channel.QueueBindAsync(deadLetterQueue, RabbitMqTopology.DeadLetterExchange, routingKey: queue, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = RabbitMqTopology.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = queue,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (var messageType in messageTypes)
        {
            var exchange = RabbitMqTopology.ExchangeFor(messageType);
            await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await channel.QueueBindAsync(queue, exchange, routingKey: messageType, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleAsync(
        IChannel channel,
        string queue,
        BasicDeliverEventArgs delivery,
        Dictionary<string, IntegrationEventInvoker> invokers,
        CancellationToken cancellationToken)
    {
        var properties = delivery.BasicProperties;
        var messageType = properties.Type;

        using var activity = MessagingTelemetry.StartProcess(messageType ?? "unknown", properties.MessageId, ReadTraceParent(properties), queue);

        if (messageType is null
            || !invokers.TryGetValue(messageType, out var invoker)
            || !Guid.TryParse(properties.MessageId, out var messageId))
        {
            LogUnreadable(logger, properties.MessageId, messageType);
            await DeadLetterAsync(channel, delivery, activity, cancellationToken).ConfigureAwait(false);
            return;
        }

        var body = delivery.Body.ToArray();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await invoker.InvokeAsync(scopeFactory, messageId, body, logger, cancellationToken).ConfigureAwait(false);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Left unacknowledged: the broker redelivers it once the channel closes.
                return;
            }
            catch (JsonException exception)
            {
                LogUnreadablePayload(logger, exception, messageId, messageType);
                await DeadLetterAsync(channel, delivery, activity, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (attempt < _options.RetryDelays.Length)
            {
                LogRetrying(logger, exception, messageId, messageType, attempt + 1, _options.RetryDelays[attempt]);
            }
            catch (Exception exception)
            {
                LogDeadLettered(logger, exception, messageId, messageType, attempt + 1);
                await DeadLetterAsync(channel, delivery, activity, cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await Task.Delay(_options.RetryDelays[attempt], timeProvider, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task DeadLetterAsync(IChannel channel, BasicDeliverEventArgs delivery, Activity? activity, CancellationToken cancellationToken)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadTraceParent(IReadOnlyBasicProperties properties)
        => properties.Headers?.TryGetValue(MessagingTelemetry.TraceParentHeader, out var value) == true
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => null,
            }
            : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming {Queue} for {EventTypeCount} event types")]
    private static partial void LogConsuming(ILogger logger, string queue, int eventTypeCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Consumer stopped; reconnecting in {Delay}")]
    private static partial void LogConsumerFailed(ILogger logger, Exception exception, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dead-lettering message {MessageId} of type {MessageType}: no handler or no valid message id")]
    private static partial void LogUnreadable(ILogger logger, string? messageId, string? messageType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettering message {MessageId} ({MessageType}): the payload cannot be read")]
    private static partial void LogUnreadablePayload(ILogger logger, Exception exception, Guid messageId, string messageType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling message {MessageId} ({MessageType}) failed on attempt {Attempt}; retrying in {Delay}")]
    private static partial void LogRetrying(ILogger logger, Exception exception, Guid messageId, string messageType, int attempt, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dead-lettering message {MessageId} ({MessageType}) after {Attempts} attempts")]
    private static partial void LogDeadLettered(ILogger logger, Exception exception, Guid messageId, string messageType, int attempts);
}
