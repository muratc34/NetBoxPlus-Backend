using Microsoft.Extensions.Logging;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Shared.Application.Behaviors;

/// <summary>
/// Logs every request by type name with its duration and, on failure, its error code. Request
/// contents are never logged. Exceptions are not logged here: they propagate to the HTTP layer,
/// which handles them in one place.
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    TimeProvider timeProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var startedAt = timeProvider.GetTimestamp();

        LogHandling(logger, requestName);

        var result = await next(cancellationToken).ConfigureAwait(false);
        var elapsedMilliseconds = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;

        if (result.IsSuccess)
        {
            LogHandled(logger, requestName, elapsedMilliseconds);
        }
        else
        {
            LogFailed(logger, requestName, result.Error.Code, result.Error.Type, elapsedMilliseconds);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogHandled(ILogger logger, string requestName, double elapsedMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "{RequestName} failed with {ErrorCode} ({ErrorType}) in {ElapsedMilliseconds:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string requestName, string errorCode, ErrorType errorType, double elapsedMilliseconds);
}
