using Shared.Application.Cqrs;
using Shared.Application.Validation;
using Shared.Domain;

namespace Shared.Application.Behaviors;

/// <summary>
/// Runs every validator registered for the request. If any field is invalid the handler is not
/// called and a failed result carrying a <see cref="ValidationError"/> is returned — no exception.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TResponse : Result, IFailureResult<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var failures = validators
            .SelectMany(validator => validator.Validate(request))
            .ToList();

        return failures.Count == 0
            ? next(cancellationToken)
            : Task.FromResult(TResponse.Failure(new ValidationError(failures)));
    }
}
