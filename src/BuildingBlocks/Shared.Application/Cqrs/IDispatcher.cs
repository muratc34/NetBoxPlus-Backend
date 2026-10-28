using Shared.Domain;

namespace Shared.Application.Cqrs;

/// <summary>
/// Entry point for commands and queries. Each call runs the registered pipeline behaviors and then
/// the request's single handler.
/// </summary>
public interface IDispatcher
{
    Task<Result> Send(ICommand command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> Query<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
