using Shared.Domain;

namespace Shared.Application.Cqrs;

/// <summary>The next step of the pipeline: another behavior, or the handler itself.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

/// <summary>
/// Wraps the handling of a request. Behaviors run in registration order, the first registered
/// being the outermost.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TResponse : Result
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
