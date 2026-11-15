using System.Diagnostics.Metrics;
using MessagingTests.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging.Tests;

/// <summary>A dead-lettered message must not be lost silently: it shows in a metric, an error log and the health check.</summary>
[Collection(nameof(RabbitMqCollection))]
public sealed class MonitoringTests(RabbitMqFixture rabbit)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeadLetteredMessage_IsCounted_LoggedOnce_AndDegradesTheService()
    {
        await using var host = await rabbit.StartHostAsync();
        using var depth = new MetricCollector<long>(host.Provider.GetRequiredService<IMeterFactory>(), MessagingMetrics.MeterName, "netboxplus.messaging.dead_letter_depth");
        var logger = new FakeLogger<DeadLetterMonitor>();
        var monitor = Monitor(host, logger);

        await host.PublishAsync(new ShipmentDelayed(Guid.CreateVersion7()));
        await host.WaitForMessagesAsync(host.DeadLetterQueue, 1, Timeout);
        await monitor.SampleAsync(Cancellation);
        await monitor.SampleAsync(Cancellation);
        depth.RecordObservableInstruments();

        Assert.Equal(1, depth.LastMeasurement!.Value);
        Assert.Equal(host.DeadLetterQueue, depth.LastMeasurement.Tags["queue"]);
        Assert.Equal(1, logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Error));
        Assert.Equal(HealthStatus.Degraded, await Health(host));

        await host.PublishAsync(new ShipmentDelayed(Guid.CreateVersion7()));
        await host.WaitForMessagesAsync(host.DeadLetterQueue, 2, Timeout);
        await monitor.SampleAsync(Cancellation);
        Assert.Equal(2, logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Error));

        await Purge(host, host.DeadLetterQueue);
        await monitor.SampleAsync(Cancellation);
        Assert.Equal(LogLevel.Information, logger.LatestRecord.Level);
        Assert.Equal(HealthStatus.Healthy, await Health(host));
    }

    [Fact]
    public async Task AnEmptyDeadLetterQueue_IsNoAlarm()
    {
        await using var host = await rabbit.StartHostAsync();
        var logger = new FakeLogger<DeadLetterMonitor>();

        await Monitor(host, logger).SampleAsync(Cancellation);

        Assert.Empty(logger.Collector.GetSnapshot());
        Assert.Equal(0, host.Provider.GetRequiredService<MessagingMetrics>().DeadLetters!.Messages);
        Assert.Equal(HealthStatus.Healthy, await Health(host));
    }

    [Fact]
    public async Task AQueueNotDeclaredYet_IsNotCounted()
    {
        await using var host = await rabbit.StartHostAsync();
        var metrics = new MessagingMetrics(host.Provider.GetRequiredService<IMeterFactory>());
        var options = Options.Create(new RabbitMqOptions { ConnectionString = host.ConnectionString, ServiceName = $"absent-{Guid.NewGuid():N}" });

        var counted = await new DeadLetterMonitor(host.Provider.GetRequiredService<RabbitMqConnection>(), options, metrics, TimeProvider.System, NullLogger<DeadLetterMonitor>.Instance, Subscriptions(host))
            .SampleAsync(Cancellation);

        Assert.False(counted);
        Assert.Null(metrics.DeadLetters);
    }

    [Fact]
    public async Task AServiceThatConsumesNothing_HasNothingToWatch()
    {
        await using var host = await rabbit.StartHostAsync();
        var metrics = new MessagingMetrics(host.Provider.GetRequiredService<IMeterFactory>());
        using var monitor = new DeadLetterMonitor(
            host.Provider.GetRequiredService<RabbitMqConnection>(),
            host.Provider.GetRequiredService<IOptions<RabbitMqOptions>>(),
            metrics,
            TimeProvider.System,
            NullLogger<DeadLetterMonitor>.Instance,
            subscriptions: null);

        await monitor.StartAsync(Cancellation);
        await Task.Delay(300, Cancellation);
        await monitor.StopAsync(Cancellation);

        Assert.Null(metrics.DeadLetters);
    }

    [Fact]
    public async Task TheMonitor_CountsShortlyAfterStart()
    {
        await using var host = await rabbit.StartHostAsync();
        var metrics = host.Provider.GetRequiredService<MessagingMetrics>();

        // The consumer may declare the queue just after the first look; the next one is five seconds later.
        for (var i = 0; i < 600 && metrics.DeadLetters is null; i++)
        {
            await Task.Delay(25, Cancellation);
        }

        Assert.Equal(new DeadLetterCount(host.DeadLetterQueue, 0), metrics.DeadLetters);
    }

    [Fact]
    public async Task TheHealthCheck_IsRegisteredForReadiness()
    {
        await using var host = await rabbit.StartHostAsync();

        var check = Assert.Single(host.Provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations, registration => registration.Name == "dead-letters");
        Assert.Contains("ready", check.Tags);
        Assert.IsType<DeadLetterHealthCheck>(check.Factory(host.Provider));
    }

    [Fact]
    public async Task Deliveries_AreCountedByTypeAndOutcome_AndTimed()
    {
        await using var host = await rabbit.StartHostAsync();
        var meters = host.Provider.GetRequiredService<IMeterFactory>();
        using var handled = new MetricCollector<long>(meters, MessagingMetrics.MeterName, "netboxplus.messages.handled");
        using var duration = new MetricCollector<double>(meters, MessagingMetrics.MeterName, "netboxplus.messages.handling_duration");

        await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 10));
        await host.Deliveries.WaitForAsync(1, Timeout);
        await host.PublishAsync(new ShipmentDelayed(Guid.CreateVersion7()));
        await host.WaitForMessagesAsync(host.DeadLetterQueue, 1, Timeout);
        await host.PublishRawAsync(IntegrationEvents.MessageTypeOf(typeof(OrderPlaced)), "{}", messageId: "not-a-guid");
        await host.WaitForMessagesAsync(host.DeadLetterQueue, 2, Timeout);
        await handled.WaitForMeasurementsAsync(5, Timeout);

        var order = IntegrationEvents.MessageTypeOf(typeof(OrderPlaced));
        var shipment = IntegrationEvents.MessageTypeOf(typeof(ShipmentDelayed));
        Assert.Equal(1, Count(handled, order, MessageOutcomes.Processed));
        Assert.Equal(2, Count(handled, shipment, MessageOutcomes.Retried));
        Assert.Equal(1, Count(handled, shipment, MessageOutcomes.DeadLettered));
        Assert.Equal(1, Count(handled, "unknown", MessageOutcomes.DeadLettered));
        Assert.Contains(duration.GetMeasurementSnapshot(), measurement => Equals(measurement.Tags["message_type"], order) && measurement.Value >= 0);
        Assert.Contains(duration.GetMeasurementSnapshot(), measurement => Equals(measurement.Tags["message_type"], shipment));
    }

    private static long Count(MetricCollector<long> collector, string messageType, string outcome)
        => collector.GetMeasurementSnapshot()
            .Where(measurement => Equals(measurement.Tags["message_type"], messageType) && Equals(measurement.Tags["outcome"], outcome))
            .Sum(measurement => measurement.Value);

    private static DeadLetterMonitor Monitor(MessagingHost host, ILogger<DeadLetterMonitor> logger) => new(
        host.Provider.GetRequiredService<RabbitMqConnection>(),
        host.Provider.GetRequiredService<IOptions<RabbitMqOptions>>(),
        host.Provider.GetRequiredService<MessagingMetrics>(),
        TimeProvider.System,
        logger,
        Subscriptions(host));

    private static IntegrationEventSubscriptions Subscriptions(MessagingHost host) => host.Provider.GetRequiredService<IntegrationEventSubscriptions>();

    private static async Task<HealthStatus> Health(MessagingHost host)
        => (await new DeadLetterHealthCheck(host.Provider.GetRequiredService<MessagingMetrics>()).CheckHealthAsync(new HealthCheckContext(), Cancellation)).Status;

    private static async Task Purge(MessagingHost host, string queue)
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(host.ConnectionString) }.CreateConnectionAsync(Cancellation);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Cancellation);
        await channel.QueuePurgeAsync(queue, Cancellation);
    }
}
