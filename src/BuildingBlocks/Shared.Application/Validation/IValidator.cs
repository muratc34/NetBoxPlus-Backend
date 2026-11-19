namespace Shared.Application.Validation;

/// <summary>
/// Checks the shape of a request: required fields, lengths, ranges, formats. Validators are
/// synchronous and do no I/O; rules that need the database (for example "email already taken")
/// belong in the handler.
/// </summary>
public interface IValidator<in T>
{
    IEnumerable<ValidationFailure> Validate(T instance);
}
