namespace Shared.Domain;

/// <summary>
/// Lets generic code create a failed result of a type it only knows as a type parameter — for
/// example a pipeline behavior that must return either <see cref="Result"/> or
/// <see cref="Result{TValue}"/>.
/// </summary>
public interface IFailureResult<out TSelf>
    where TSelf : Result
{
    static abstract TSelf Failure(Error error);
}
