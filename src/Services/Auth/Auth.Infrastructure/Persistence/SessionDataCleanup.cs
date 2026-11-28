using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Once an hour deletes the profile unlocks of sign-ins that ended: no token of the family can be used any more.
/// </summary>
internal sealed partial class SessionDataCleanup(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<SessionDataCleanup> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
                var now = timeProvider.GetUtcNow();

                var unlocks = await context.ProfileUnlocks
                    .Where(unlock => !context.RefreshTokens.Any(token =>
                        token.FamilyId == unlock.SessionId && token.UsedAt == null && token.RevokedAt == null && token.ExpiresAt > now))
                    .ExecuteDeleteAsync(stoppingToken).ConfigureAwait(false);

                if (unlocks > 0)
                {
                    LogDeleted(logger, unlocks);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Unlocks} profile unlocks of ended sign-ins")]
    private static partial void LogDeleted(ILogger logger, int unlocks);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting the profile unlocks of ended sign-ins failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
