using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Application;

namespace Shared.Infrastructure.API.Errors;

/// <summary>
/// Turns exceptions that escape the endpoint into problem details. This is the one place where an
/// unexpected exception is logged; the response never contains its message or stack trace.
/// A database that cannot be reached (<see cref="DataUnavailableException"/>) is not a bug: it is
/// answered with 503 <c>Data.Unavailable</c> and a <c>Retry-After</c> header, and logged as a warning.
/// </summary>
internal sealed partial class UnhandledExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IOptions<ErrorResponseOptions> options,
    ILogger<UnhandledExceptionHandler> logger)
    : IExceptionHandler
{
    /// <summary>Not an HTTP status; the de-facto code for "client closed the request".</summary>
    private const int ClientClosedRequest = 499;

    /// <summary>Seconds a client is asked to wait before sending again a request that found no database connection.</summary>
    internal const int UnavailableRetryAfterSeconds = 5;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogRequestAborted(logger, httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        if (FindUnavailable(exception) is { } unavailable)
        {
            LogDataUnavailable(logger, unavailable, httpContext.Request.Method, httpContext.Request.Path);

            var problem = ErrorMapping.ToProblemDetails(DataErrors.Unavailable, options.Value);
            httpContext.Response.StatusCode = problem.Status!.Value;
            httpContext.Response.Headers.RetryAfter = UnavailableRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = problem,
            }).ConfigureAwait(false);
        }

        int status;
        if (exception is BadHttpRequestException badRequest)
        {
            status = badRequest.StatusCode;
            LogBadRequest(logger, httpContext.Request.Method, httpContext.Request.Path, status);
        }
        else
        {
            status = StatusCodes.Status500InternalServerError;
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = ReasonPhrases.GetReasonPhrase(status),
                Detail = status == StatusCodes.Status500InternalServerError
                    ? "An unexpected error occurred."
                    : "The request could not be processed.",
            },
        }).ConfigureAwait(false);
    }

    /// <summary>The exception itself or one it wraps, in case a caller on the way wrapped it.</summary>
    private static DataUnavailableException? FindUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DataUnavailableException unavailable)
            {
                return unavailable;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No database connection for {Method} {Path}; answered with 503")]
    private static partial void LogDataUnavailable(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while processing {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rejected malformed request {Method} {Path} with {StatusCode}")]
    private static partial void LogBadRequest(ILogger logger, string method, string path, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client aborted {Method} {Path}")]
    private static partial void LogRequestAborted(ILogger logger, string method, string path);
}
