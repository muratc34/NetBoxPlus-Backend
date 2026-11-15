using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Streaming.Application;
using Streaming.Domain.Playback;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure;

/// <summary>
/// Counts transcode jobs and active playback sessions for the gauges, every 30 seconds. Runs in the
/// API only: the transcoders share the database, one count is enough.
/// </summary>
internal sealed partial class StreamingMetricsSampler(
    IServiceScopeFactory scopeFactory,
    StreamingMetrics metrics,
    TimeProvider timeProvider,
    ILogger<StreamingMetricsSampler> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                await Task.Delay(Interval, timeProvider, stoppingToken).ConfigureAwait(false);
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
        var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
        var now = timeProvider.GetUtcNow();
        var jobs = context.Set<TranscodeJob>().AsNoTracking();
        var heardFrom = now - PlaybackSession.Timeout;

        var running = await jobs.LongCountAsync(job => job.LeasedUntil != null && job.LeasedUntil >= now, cancellationToken).ConfigureAwait(false);
        var waiting = await jobs.LongCountAsync(job => job.LeasedUntil == null || job.LeasedUntil < now, cancellationToken).ConfigureAwait(false);
        var active = await context.PlaybackSessions.AsNoTracking()
            .LongCountAsync(session => session.EndedAt == null && session.LastHeartbeatAt >= heardFrom, cancellationToken)
            .ConfigureAwait(false);

        metrics.Latest = new StreamingSnapshot(waiting, running, active);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Counting transcode jobs and playback sessions failed")]
    private static partial void LogSampleFailed(ILogger logger, Exception exception);
}
