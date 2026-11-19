using Catalog.Application.Abstractions;
using Catalog.Domain.Images;
using Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure.Images;

/// <summary>Removes uploads that were never finished and images retired long enough ago.</summary>
internal sealed class ImageCleaner(CatalogDbContext context, IImageStorage storage, TimeProvider timeProvider)
{
    private const int BatchSize = 200;

    /// <summary>One pass; returns how many image files were removed.</summary>
    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var retiredBefore = now - ImageFile.RetiredGracePeriod;

        var files = await context.Set<ImageFile>()
            .Where(file => (file.Status == ImageFileStatus.Pending && file.ExpiresAt <= now)
                || (file.Status == ImageFileStatus.Retired && file.RetiredAt <= retiredBefore))
            .OrderBy(file => file.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var file in files)
        {
            await storage.DeleteUploadAsync(file.Id, cancellationToken).ConfigureAwait(false);
            if (file.Name is not null)
            {
                await storage.DeletePublishedAsync(file.Name, cancellationToken).ConfigureAwait(false);
            }

            context.Remove(file);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return files.Count;
    }
}

/// <summary>Runs the clean-up once an hour; a failed pass is logged and tried again next time.</summary>
internal sealed partial class ImageCleanupService(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<ImageCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var removed = await scope.ServiceProvider.GetRequiredService<ImageCleaner>().CleanAsync(cancellationToken).ConfigureAwait(false);
            LogCleaned(logger, removed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Image clean-up removed {Count} files")]
    private static partial void LogCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Image clean-up failed; it runs again at the next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
