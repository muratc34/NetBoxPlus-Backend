using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Infrastructure.Persistence.Outbox;

/// <summary>
/// Counts the outbox now and then for the metrics and the health check. A message given up on is
/// an event no consumer will ever see, so it is logged as an error once, when the number changes,
/// and the service reports itself degraded until someone deals with it.
/// </summary>
internal sealed partial class OutboxMonitor<TContext>(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    OutboxMetrics metrics,
    TimeProvider timeProvider,
    ILogger<OutboxMonitor<TContext>> logger)
    : BackgroundService
    where TContext : ServiceDbContext
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A process that does not send (the transcoder) leaves the counting to the one that does.
        if (!_options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogSampleFailed(logger, exception);
            }

            try
            {
                await Task.Delay(_options.MonitorInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task SampleAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var unsent = context.OutboxMessages.AsNoTracking().Where(message => message.ProcessedAt == null);

        var pending = await unsent.LongCountAsync(message => message.Attempts < _options.MaxAttempts, cancellationToken).ConfigureAwait(false);
        var abandoned = await unsent.LongCountAsync(message => message.Attempts >= _options.MaxAttempts, cancellationToken).ConfigureAwait(false);
        var oldest = await unsent
            .Where(message => message.Attempts < _options.MaxAttempts)
            .Select(message => (DateTimeOffset?)message.OccurredAt)
            .MinAsync(cancellationToken)
            .ConfigureAwait(false);

        var age = oldest is { } occurredAt ? Math.Max(0, (timeProvider.GetUtcNow() - occurredAt).TotalSeconds) : 0;
        var previous = metrics.Latest?.Abandoned ?? 0;
        metrics.Latest = new OutboxSnapshot(pending, abandoned, age);

        if (abandoned > 0 && abandoned != previous)
        {
            LogAbandoned(logger, abandoned, _options.MaxAttempts);
        }
        else if (abandoned == 0 && previous > 0)
        {
            LogNoLongerAbandoned(logger);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Count} outbox messages were given up on after {MaxAttempts} failed sends and will not be sent; consumers never see these events. Find them where ProcessedAt is null and Attempts >= {MaxAttempts}")]
    private static partial void LogAbandoned(ILogger logger, long count, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "No outbox messages are given up on any more")]
    private static partial void LogNoLongerAbandoned(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting the outbox failed")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
