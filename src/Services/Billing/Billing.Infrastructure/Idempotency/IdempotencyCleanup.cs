using Billing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Billing.Infrastructure.Idempotency;

internal sealed class IdempotencyCleaner(BillingDbContext context, TimeProvider timeProvider)
{
    /// <summary>Deletes requests older than the retention; returns how many.</summary>
    public Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var expired = timeProvider.GetUtcNow() - EfIdempotencyStore.Retention;
        return context.Set<IdempotencyRequest>().Where(request => request.StartedAt < expired).ExecuteDeleteAsync(cancellationToken);
    }
}

internal sealed partial class IdempotencyCleanupService(IServiceScopeFactory scopes, ILogger<IdempotencyCleanupService> logger) : BackgroundService
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
                    await scope.ServiceProvider.GetRequiredService<IdempotencyCleaner>().CleanAsync(stoppingToken).ConfigureAwait(false);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cleaning up idempotency keys failed; it is tried again in an hour")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
