using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure.Playback;

/// <summary>Deletes sessions that ended, or stopped sending heartbeats, more than a day ago.</summary>
internal sealed class PlaybackSessionCleaner(StreamingDbContext context, TimeProvider timeProvider)
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(1);

    public Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var before = timeProvider.GetUtcNow() - KeptFor;
        return context.PlaybackSessions
            .Where(session => session.LastHeartbeatAt < before && (session.EndedAt == null || session.EndedAt < before))
            .ExecuteDeleteAsync(cancellationToken);
    }
}

internal sealed partial class PlaybackSessionCleanupService(IServiceScopeFactory scopes, ILogger<PlaybackSessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<PlaybackSessionCleaner>().CleanAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception);
                }

                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up old playback sessions failed; it is tried again in an hour")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
