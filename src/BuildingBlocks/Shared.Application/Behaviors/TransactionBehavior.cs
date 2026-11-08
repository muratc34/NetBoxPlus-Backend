using Shared.Application.Cqrs;
using Shared.Domain;

namespace Shared.Application.Behaviors;

/// <summary>
/// Saves the unit of work after a command succeeds; a failed command saves nothing. A save that
/// loses a race with another write becomes a failed result, not a server error. Applies to
/// commands only — the constraint keeps it out of query pipelines.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase
    where TResponse : Result, IFailureResult<TResponse>
{
    private static readonly Error Conflict = Error.Conflict(
        "Data.Conflict",
        "The change conflicts with another one made at the same time. Reload and try again.");

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var result = await next(cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return result;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DataConflictException)
        {
            return TResponse.Failure(Conflict);
        }

        return result;
    }
}
