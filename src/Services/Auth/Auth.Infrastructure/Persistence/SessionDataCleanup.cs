using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Infrastructure.Persistence;

/// <summary>
/// Once an hour deletes what sign-ins leave behind:
/// <list type="bullet">
/// <item>Passkey challenges an hour past their expiry. Anyone may ask for sign-in options and every request keeps a row
/// for five minutes; the hour lets a late answer find its challenge used or expired rather than unknown.</item>
/// <item>Profile unlocks of sign-ins that ended: no token of the family can be used any more.</item>
/// </list>
/// </summary>
internal sealed partial class SessionDataCleanup(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<SessionDataCleanup> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private static readonly TimeSpan ChallengesKeptAfterExpiry = TimeSpan.FromHours(1);

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
                var before = now - ChallengesKeptAfterExpiry;

                var challenges = await context.PasskeyChallenges
                    .Where(challenge => challenge.ExpiresAt < before)
                    .ExecuteDeleteAsync(stoppingToken).ConfigureAwait(false);
                var unlocks = await context.ProfileUnlocks
                    .Where(unlock => !context.RefreshTokens.Any(token =>
                        token.FamilyId == unlock.SessionId && token.UsedAt == null && token.RevokedAt == null && token.ExpiresAt > now))
                    .ExecuteDeleteAsync(stoppingToken).ConfigureAwait(false);

                if (challenges + unlocks > 0)
                {
                    LogDeleted(logger, challenges, unlocks);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Challenges} expired passkey challenges and {Unlocks} profile unlocks of ended sign-ins")]
    private static partial void LogDeleted(ILogger logger, int challenges, int unlocks);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Deleting expired sign-in data failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
