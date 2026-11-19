using Shared.Domain;

namespace Shared.Application.Validation;

/// <summary>
/// An <see cref="Error"/> of type <see cref="ErrorType.Validation"/> that carries every invalid
/// field of a request, so that clients see all problems at once.
/// </summary>
public sealed record ValidationError : Error
{
    public const string ErrorCode = "Validation.Failed";

    public ValidationError(IReadOnlyList<ValidationFailure> failures)
        : base(ErrorCode, "One or more validation errors occurred.", ErrorType.Validation)
    {
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 0)
        {
            throw new ArgumentException("A validation error needs at least one failure.", nameof(failures));
        }

        Failures = failures;
    }

    public IReadOnlyList<ValidationFailure> Failures { get; }
}
