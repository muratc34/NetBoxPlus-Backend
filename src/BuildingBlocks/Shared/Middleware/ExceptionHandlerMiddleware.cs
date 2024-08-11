using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shared.Domain.Errors;
using Shared.Domain.Result;
using Shared.Exceptions;
using System.Net;
using System.Text.Json;

namespace Shared.Middleware;

public class ExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger;

    public ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Exception occurred: {Message}", exception.Message);

            await HandleExceptionAsync(context, exception);
        }
    }

    private async static Task HandleExceptionAsync(HttpContext httpContext, Exception exception)
    {
        (HttpStatusCode httpStatusCode, IReadOnlyCollection<Error> errors) = GetHttpStatusCodeAndErrors(exception);

        httpContext.Response.ContentType = "application/json";

        httpContext.Response.StatusCode = (int)httpStatusCode;

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        string response = JsonSerializer.Serialize(new ApiErrorResponse(errors), serializerOptions);

        await httpContext.Response.WriteAsync(response);
    }

    private static (HttpStatusCode httpStatusCode, IReadOnlyCollection<Error>) GetHttpStatusCodeAndErrors(Exception exception) =>
            exception switch
            {
                ValidationException validationException => (HttpStatusCode.BadRequest, validationException.Errors),
                DomainException domainException => (HttpStatusCode.BadRequest, new[] { domainException.Error }),
                _ => (HttpStatusCode.InternalServerError, new[] { DomainErrors.General.ServerError })
            };

    public class ApiErrorResponse
    {
        public ApiErrorResponse(IReadOnlyCollection<Error> errors) => Errors = errors;
        public IReadOnlyCollection<Error> Errors { get; }
    }
}
