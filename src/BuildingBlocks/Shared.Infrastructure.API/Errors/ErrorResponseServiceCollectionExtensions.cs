using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.API.Errors;

internal static class ErrorResponseServiceCollectionExtensions
{
    public const string TraceIdExtension = "traceId";

    /// <summary>
    /// Problem details for every error the application returns: <see cref="Shared.Domain.Error"/>
    /// results, unhandled exceptions and empty 4xx/5xx responses (such as 401 from authentication).
    /// Each response carries the request path as <c>instance</c> and the W3C trace id.
    /// </summary>
    public static IServiceCollection AddErrorResponses(this IServiceCollection services)
    {
        services.AddOptions<ErrorResponseOptions>()
            .BindConfiguration(ErrorResponseOptions.SectionName)
            .Validate(
                options => options.TypeBaseUri is { IsAbsoluteUri: true } uri && uri.AbsoluteUri.EndsWith('/'),
                $"{ErrorResponseOptions.SectionName}:{nameof(ErrorResponseOptions.TypeBaseUri)} must be an absolute URI ending with '/'.")
            .ValidateOnStart();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions.TryAdd(
                TraceIdExtension,
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        });

        services.AddExceptionHandler<UnhandledExceptionHandler>();

        return services;
    }
}
