using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application.Cqrs;
using Streaming.Application.Videos.FinishTranscode;
using Streaming.Domain.Videos;
using Streaming.Infrastructure.Persistence;

namespace Streaming.Infrastructure.Media;

/// <summary>
/// Converts one queued video at a time. The job is taken with <c>FOR UPDATE SKIP LOCKED</c> and a
/// lease, so several workers never take the same video and a dead worker's video is taken again.
/// A file that cannot be converted fails at once; trouble around it (storage, a stopped worker) is
/// tried again a few times, later each time.
/// </summary>
internal sealed partial class TranscodeProcessor(
    StreamingDbContext context,
    VideoBlobStorage storage,
    FfmpegTranscoder transcoder,
    IDispatcher dispatcher,
    IOptions<TranscoderOptions> options,
    TimeProvider timeProvider,
    ILogger<TranscodeProcessor> logger)
{
    public const int MaxAttempts = 3;

    /// <summary>Longer than any conversion; a worker that is still alive finishes well within it.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromHours(2);

    private sealed record LeasedJob(Guid VideoId, int Attempts);

    /// <summary>Converts the next video in line; returns whether there was one.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var leaseEnd = now + Lease;
        var jobs = await context.Database.SqlQuery<LeasedJob>(
            $"""
            UPDATE "TranscodeJobs" SET "LeasedUntil" = {leaseEnd}, "Attempts" = "Attempts" + 1
            WHERE "VideoId" = (
                SELECT "VideoId" FROM "TranscodeJobs"
                WHERE "AvailableAt" <= {now} AND ("LeasedUntil" IS NULL OR "LeasedUntil" < {now})
                ORDER BY "AvailableAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "VideoId", "Attempts"
            """).ToListAsync(cancellationToken).ConfigureAwait(false);

        if (jobs.Count == 0)
        {
            return false;
        }

        var job = jobs[0];
        var work = Path.Combine(options.Value.WorkDirectory ?? Path.GetTempPath(), "netboxplus-transcode", job.VideoId.ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var source = Path.Combine(work, "source");
            var output = Directory.CreateDirectory(Path.Combine(work, "output")).FullName;

            await storage.DownloadUploadAsync(job.VideoId, source, cancellationToken).ConfigureAwait(false);
            var outcome = await transcoder.TranscodeAsync(source, output, cancellationToken).ConfigureAwait(false);

            if (outcome is TranscodeOutcome.Converted converted)
            {
                await storage.UploadOutputAsync(job.VideoId, output, cancellationToken).ConfigureAwait(false);
                await FinishAsync(job.VideoId, new MarkVideoReadyCommand(job.VideoId, converted.DurationSeconds, converted.Renditions), cancellationToken).ConfigureAwait(false);
                LogConverted(logger, job.VideoId, converted.Renditions.Count);
            }
            else if (outcome is TranscodeOutcome.Rejected rejected)
            {
                await FinishAsync(job.VideoId, new MarkVideoFailedCommand(job.VideoId, rejected.Reason), cancellationToken).ConfigureAwait(false);
                LogRejected(logger, job.VideoId, rejected.Reason);
            }

            await storage.DeleteUploadAsync(job.VideoId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RetryOrGiveUpAsync(job, exception, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
                // Left for the system's temporary folder cleanup.
            }
        }

        return true;
    }

    private async Task FinishAsync(Guid videoId, ICommand command, CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(command, cancellationToken).ConfigureAwait(false);

        // Not queued any more (finished elsewhere, or removed): the job has nothing left to do.
        if (result.IsFailure && result.Error == VideoErrors.NotQueued)
        {
            await context.Set<TranscodeJob>().Where(item => item.VideoId == videoId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"The result of video {videoId} could not be saved: {result.Error.Code}.");
        }
    }

    private async Task RetryOrGiveUpAsync(LeasedJob job, Exception exception, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        if (job.Attempts >= MaxAttempts)
        {
            LogGaveUp(logger, exception, job.VideoId, job.Attempts);
            await dispatcher.Send(new MarkVideoFailedCommand(job.VideoId, $"Converting failed {job.Attempts} times."), cancellationToken).ConfigureAwait(false);
            return;
        }

        var delay = TimeSpan.FromMinutes(job.Attempts * job.Attempts);
        var availableAt = timeProvider.GetUtcNow() + delay;
        var message = exception.Message.Length > TranscodeJob.LastErrorMaxLength ? exception.Message[..TranscodeJob.LastErrorMaxLength] : exception.Message;
        await context.Set<TranscodeJob>()
            .Where(item => item.VideoId == job.VideoId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.AvailableAt, availableAt).SetProperty(item => item.LeasedUntil, (DateTimeOffset?)null).SetProperty(item => item.LastError, message),
                cancellationToken)
            .ConfigureAwait(false);
        LogRetrying(logger, exception, job.VideoId, job.Attempts, delay.TotalMinutes);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Converted video {VideoId} into {Renditions} renditions")]
    private static partial void LogConverted(ILogger logger, Guid videoId, int renditions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Video {VideoId} cannot be converted: {Reason}")]
    private static partial void LogRejected(ILogger logger, Guid videoId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Converting video {VideoId} failed on attempt {Attempts}; trying again in {DelayMinutes} min")]
    private static partial void LogRetrying(ILogger logger, Exception exception, Guid videoId, int attempts, double delayMinutes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Gave up on converting video {VideoId} after {Attempts} attempts")]
    private static partial void LogGaveUp(ILogger logger, Exception exception, Guid videoId, int attempts);
}

/// <summary>Runs the processor continuously; idle, it looks again every few seconds.</summary>
internal sealed partial class TranscodeWorker(IServiceScopeFactory scopes, ILogger<TranscodeWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = false;
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    worked = await scope.ServiceProvider.GetRequiredService<TranscodeProcessor>().ProcessNextAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception);
                }

                if (!worked)
                {
                    await Task.Delay(Idle, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; a video in progress is taken again when its lease runs out.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The transcode worker failed; it carries on")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Uploads never confirmed in time: the video is marked expired and the file goes.</summary>
internal sealed class ExpiredUploadCleaner(StreamingDbContext context, VideoBlobStorage storage, TimeProvider timeProvider)
{
    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expired = await context.Videos
            .Where(video => video.Status == VideoStatus.AwaitingUpload && video.UploadExpiresAt <= now)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var video in expired)
        {
            await storage.DeleteUploadAsync(video.Id, cancellationToken).ConfigureAwait(false);
            video.Expire(now);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return expired.Count;
    }
}

internal sealed partial class ExpiredUploadCleanupService(IServiceScopeFactory scopes, ILogger<ExpiredUploadCleanupService> logger) : BackgroundService
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
                    await scope.ServiceProvider.GetRequiredService<ExpiredUploadCleaner>().CleanAsync(stoppingToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up expired video uploads failed; it is tried again in an hour")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
