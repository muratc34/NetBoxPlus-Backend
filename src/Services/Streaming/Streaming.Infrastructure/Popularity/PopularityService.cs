using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;
using Shared.Application.Cqrs;
using Streaming.Application.Popularity;

namespace Streaming.Infrastructure.Popularity;

public sealed class PopularityOptions
{
    public const string SectionName = "Popularity";

    /// <summary>How often the ranking is computed; an hour by default.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Every instance runs the loop; one that wakes up shortly after another computed does nothing.
    /// The margin lets an instance whose timer runs a little early still take its turn.
    /// </summary>
    internal TimeSpan MinimumGap => Interval - (Interval / 12);
}

/// <summary>Computes the popularity ranking at start and then every <see cref="PopularityOptions.Interval"/>.</summary>
internal sealed partial class PopularityService(
    IServiceScopeFactory scopes,
    IOptions<PopularityOptions> options,
    TimeProvider timeProvider,
    ILogger<PopularityService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
                        .Send(new ComputePopularityCommand(options.Value.MinimumGap), stoppingToken)
                        .ConfigureAwait(false);
                    if (result.IsFailure && result.Error != DataErrors.Conflict)
                    {
                        LogRejected(logger, result.Error.Code);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception);
                }

                await Task.Delay(interval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Computing the popularity ranking failed; it is tried again at the next interval")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Computing the popularity ranking was rejected with {ErrorCode}")]
    private static partial void LogRejected(ILogger logger, string errorCode);
}
