using Shared.Domain;

namespace Shared.Application.Cqrs;

/// <summary>Handles a query.</summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}
