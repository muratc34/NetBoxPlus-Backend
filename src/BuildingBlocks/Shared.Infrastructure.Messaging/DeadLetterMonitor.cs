using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Exceptions;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// Counts the service's dead-letter queue now and then. A dead-lettered message is an event a
/// handler could not take; losing it silently is the worst case, so a change in the count is
/// logged as an error once, and the service reports itself degraded while any are there.
/// </summary>
internal sealed partial class DeadLetterMonitor(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    MessagingMetrics metrics,
    TimeProvider timeProvider,
    ILogger<DeadLetterMonitor> logger,
    IntegrationEventSubscriptions? subscriptions = null)
    : BackgroundService
{
    /// <summary>How soon to look again while the consumer has not declared the queue yet.</summary>
    internal static readonly TimeSpan NotDeclaredRetry = TimeSpan.FromSeconds(5);

    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A service that consumes nothing has no dead-letter queue.
        if (subscriptions is null || subscriptions.EventTypes.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var counted = true;
            try
            {
                counted = await SampleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The broker being away already shows in the rabbitmq health check.
                LogSampleFailed(logger, exception);
            }

            try
            {
                var wait = counted || NotDeclaredRetry > _options.DeadLetterCheckInterval ? _options.DeadLetterCheckInterval : NotDeclaredRetry;
                await Task.Delay(wait, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Counts the queue; returns <see langword="false"/> when it does not exist yet.</summary>
    internal async Task<bool> SampleAsync(CancellationToken cancellationToken)
    {
        var queue = RabbitMqTopology.DeadLetterQueueFor(_options.ServiceName);
        var brokerConnection = await connection.GetAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await brokerConnection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        long messages;
        try
        {
            // Passive: only looks. The consumer declares the queue; until it has, there is nothing to count.
            messages = (await channel.QueueDeclarePassiveAsync(queue, cancellationToken).ConfigureAwait(false)).MessageCount;
        }
        catch (OperationInterruptedException)
        {
            return false;
        }

        var previous = metrics.DeadLetters?.Messages ?? 0;
        metrics.DeadLetters = new DeadLetterCount(queue, messages);

        if (messages > 0 && messages != previous)
        {
            LogDeadLetters(logger, messages, queue);
        }
        else if (messages == 0 && previous > 0)
        {
            LogNoDeadLetters(logger, queue);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} messages are in the dead-letter queue {Queue}: events a handler could not take. Look at them in the RabbitMQ management UI")]
    private static partial void LogDeadLetters(ILogger logger, long count, string queue);

    [LoggerMessage(Level = LogLevel.Information, Message = "The dead-letter queue {Queue} is empty again")]
    private static partial void LogNoDeadLetters(ILogger logger, string queue);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Counting the dead-letter queue failed")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
