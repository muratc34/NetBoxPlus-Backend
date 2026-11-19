using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Domain;

namespace Shared.Infrastructure.API.Errors;

/// <summary>
/// Writes an <see cref="Error"/> as <c>application/problem+json</c> through
/// <see cref="IProblemDetailsService"/>, so that it gets the same customization (trace id,
/// instance) as framework-generated problems.
/// </summary>
internal sealed class ErrorHttpResult(Error error) : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
{
    public Error Error { get; } = error;

    public int? StatusCode => ErrorMapping.ToStatusCode(Error.Type);

    public string ContentType => "application/problem+json";

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var options = httpContext.RequestServices.GetRequiredService<IOptions<ErrorResponseOptions>>().Value;
        var problem = ErrorMapping.ToProblemDetails(Error, options);

        httpContext.Response.StatusCode = problem.Status!.Value;

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        }).ConfigureAwait(false);
    }
}
