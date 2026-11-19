using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Shared.Application.Events;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Outbox;

namespace Shared.Infrastructure.Persistence.Tests;

/// <summary>An event given up on is lost to every consumer: it must show in the metrics, the logs and the health check.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class OutboxMonitorTests(PostgresFixture postgres)
{
    private static readonly OutboxOptions Options = new() { MaxAttempts = 3, MonitorInterval = TimeSpan.FromSeconds(30) };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheGauges_ReportWaitingAndAbandonedMessages_AndTheOldestWait()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        await Add(
            services,
            Message(now - TimeSpan.FromMinutes(5), attempts: 0),
            Message(now - TimeSpan.FromMinutes(2), attempts: 2),
            Message(now - TimeSpan.FromHours(3), attempts: 3),
            Message(now - TimeSpan.FromHours(4), attempts: 0, processedAt: now - TimeSpan.FromHours(4)));
        var meters = services.Provider.GetRequiredService<IMeterFactory>();
        using var pending = new MetricCollector<long>(meters, OutboxMetrics.MeterName, "netboxplus.outbox.pending");
        using var abandoned = new MetricCollector<long>(meters, OutboxMetrics.MeterName, "netboxplus.outbox.abandoned");
        using var age = new MetricCollector<double>(meters, OutboxMetrics.MeterName, "netboxplus.outbox.oldest_pending_age");

        await Monitor(services).SampleAsync(Cancellation);
        pending.RecordObservableInstruments();
        abandoned.RecordObservableInstruments();
        age.RecordObservableInstruments();

        Assert.Equal(2, pending.LastMeasurement!.Value);
        Assert.Equal(1, abandoned.LastMeasurement!.Value);
        Assert.Equal(300, age.LastMeasurement!.Value, precision: 3);
    }

    [Fact]
    public async Task BeforeTheFirstCount_NothingIsReported_AndTheServiceIsHealthy()
    {
        await using var services = await postgres.CreateServicesAsync();
        using var pending = new MetricCollector<long>(services.Provider.GetRequiredService<IMeterFactory>(), OutboxMetrics.MeterName, "netboxplus.outbox.pending");

        pending.RecordObservableInstruments();

        Assert.Empty(pending.GetMeasurementSnapshot());
        Assert.Equal(HealthStatus.Healthy, await Health(services));
    }

    [Fact]
    public async Task AbandonedMessages_AreLoggedOncePerChange_AndDegradeTheService()
    {
        await using var services = await postgres.CreateServicesAsync();
        var now = services.Time.GetUtcNow();
        var logger = new FakeLogger<OutboxMonitor<TestDbContext>>();
        var monitor = Monitor(services, logger);
        var first = Message(now, attempts: 3);
        await Add(services, first);

        await monitor.SampleAsync(Cancellation);
        await monitor.SampleAsync(Cancellation);
        Assert.Equal(1, logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Error));
        Assert.Equal(HealthStatus.Degraded, await Health(services));

        await Add(services, Message(now, attempts: 5));
        await monitor.SampleAsync(Cancellation);
        Assert.Equal(2, logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Error));

        await using (var scope = services.Scope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.ExecuteDeleteAsync(Cancellation);
        }

        await monitor.SampleAsync(Cancellation);
        Assert.Equal(LogLevel.Information, logger.LatestRecord.Level);
        Assert.Equal(HealthStatus.Healthy, await Health(services));
    }

    [Fact]
    public async Task WaitingMessages_AloneAreNotAnAlarm()
    {
        await using var services = await postgres.CreateServicesAsync();
        var logger = new FakeLogger<OutboxMonitor<TestDbContext>>();
        await Add(services, Message(services.Time.GetUtcNow(), attempts: 2));

        await Monitor(services, logger).SampleAsync(Cancellation);

        Assert.Empty(logger.Collector.GetSnapshot());
        Assert.Equal(HealthStatus.Healthy, await Health(services));
    }

    [Fact]
    public async Task TheMonitor_CountsAfterStart_AndThenEveryInterval_UnlessSendingIsOff()
    {
        await using var services = await postgres.CreateServicesAsync();
        var metrics = services.Provider.GetRequiredService<OutboxMetrics>();
        using var monitor = Monitor(services);

        await monitor.StartAsync(Cancellation);
        try
        {
            await Until(() => metrics.Latest is not null);
            await Add(services, Message(services.Time.GetUtcNow(), attempts: 0));
            services.Time.Advance(Options.MonitorInterval);
            await Until(() => metrics.Latest is { Pending: 1 });
        }
        finally
        {
            await monitor.StopAsync(Cancellation);
        }

        await using var disabled = await postgres.CreateServicesAsync();
        using var off = Monitor(disabled, options: new OutboxOptions { Enabled = false });
        await off.StartAsync(Cancellation);
        disabled.Time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(200, Cancellation);
        await off.StopAsync(Cancellation);
        Assert.Null(disabled.Provider.GetRequiredService<OutboxMetrics>().Latest);
    }

    [Fact]
    public async Task TheMonitor_AndItsHealthCheck_AreRegistered()
    {
        await using var services = await postgres.CreateServicesAsync();

        Assert.Single(services.Provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OutboxMonitor<TestDbContext>>());
        var check = Assert.Single(
            services.Provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            registration => registration.Name == "outbox");
        Assert.Contains("ready", check.Tags);
        Assert.IsType<OutboxHealthCheck>(check.Factory(services.Provider));
    }

    [Fact]
    public async Task TheProcessor_CountsSentMessages_AndFailedSends()
    {
        await using var services = await postgres.CreateServicesAsync();
        using var sent = new MetricCollector<long>(services.Provider.GetRequiredService<IMeterFactory>(), OutboxMetrics.MeterName, "netboxplus.outbox.sent");
        using var failures = new MetricCollector<long>(services.Provider.GetRequiredService<IMeterFactory>(), OutboxMetrics.MeterName, "netboxplus.outbox.send_failures");
        await Add(services, Message(services.Time.GetUtcNow(), attempts: 0), Message(services.Time.GetUtcNow(), attempts: 0), Message(services.Time.GetUtcNow(), attempts: 0));
        services.Transport.FailWith = new InvalidOperationException("The broker refused it.");
        services.Transport.FailAfter = 2;

        await Processor(services).ProcessBatchAsync(Cancellation);

        Assert.Equal(2, sent.GetMeasurementSnapshot().EvaluateAsCounter());
        Assert.Equal(1, failures.GetMeasurementSnapshot().EvaluateAsCounter());
    }

    private static OutboxMonitor<TestDbContext> Monitor(TestServices services, ILogger<OutboxMonitor<TestDbContext>>? logger = null, OutboxOptions? options = null) => new(
        services.Provider.GetRequiredService<IServiceScopeFactory>(),
        Microsoft.Extensions.Options.Options.Create(options ?? Options),
        services.Provider.GetRequiredService<OutboxMetrics>(),
        services.Time,
        logger ?? NullLogger<OutboxMonitor<TestDbContext>>.Instance);

    private static OutboxProcessor<TestDbContext> Processor(TestServices services) => new(
        services.Provider.GetRequiredService<IServiceScopeFactory>(),
        Microsoft.Extensions.Options.Options.Create(Options),
        services.Provider.GetRequiredService<OutboxMetrics>(),
        services.Time,
        NullLogger<OutboxProcessor<TestDbContext>>.Instance);

    private static async Task<HealthStatus> Health(TestServices services)
        => (await new OutboxHealthCheck(services.Provider.GetRequiredService<OutboxMetrics>()).CheckHealthAsync(new HealthCheckContext(), Cancellation)).Status;

    private static OutboxMessage Message(DateTimeOffset occurredAt, int attempts, DateTimeOffset? processedAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = IntegrationEvents.MessageTypeOf(typeof(PriceChanged)),
        Payload = "{}",
        OccurredAt = occurredAt,
        Attempts = attempts,
        ProcessedAt = processedAt,
    };

    private static async Task Add(TestServices services, params OutboxMessage[] messages)
    {
        await using var scope = services.Scope();
        var context = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        context.OutboxMessages.AddRange(messages);
        await context.SaveChangesAsync(Cancellation);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25, Cancellation);
        }

        Assert.True(condition());
    }
}
