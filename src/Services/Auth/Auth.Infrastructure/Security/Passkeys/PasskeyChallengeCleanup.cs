using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Security.Passkeys;

/// <summary>
/// Deletes passkey challenges that expired, once an hour. Anyone may ask for sign-in options, and every request
/// keeps a row for five minutes; without this they would pile up. Rows are kept an hour past their expiry, so a
/// late answer still finds its challenge used or expired rather than unknown.
/// </summary>
internal sealed partial class PasskeyChallengeCleanup(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<PasskeyChallengeCleanup> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private static readonly TimeSpan KeptAfterExpiry = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var before = timeProvider.GetUtcNow() - KeptAfterExpiry;
                var deleted = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().PasskeyChallenges
                    .Where(challenge => challenge.ExpiresAt < before)
                    .ExecuteDeleteAsync(stoppingToken).ConfigureAwait(false);
                if (deleted > 0)
                {
                    LogDeleted(logger, deleted);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired passkey challenges")]
    private static partial void LogDeleted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting expired passkey challenges failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
