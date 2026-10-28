using Shared.Application.Cqrs;
using Shared.Domain;

namespace Shared.Application.Behaviors;

/// <summary>
/// Saves the unit of work after a command succeeds; a failed command saves nothing. Applies to
/// commands only — the constraint keeps it out of query pipelines.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
