namespace Shared.Domain;

/// <summary>
/// Category of an <see cref="Error"/>. The HTTP layer maps each category to a status code, so this
/// enum is the only place that decides how a failure is reported to clients.
/// </summary>
public enum ErrorType
{
    /// <summary>Reserved for <see cref="Error.None"/>.</summary>
    None = 0,
    Failure,
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
}
