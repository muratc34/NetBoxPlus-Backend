namespace Shared.Domain;

/// <summary>
/// Describes why an operation failed. <see cref="Code"/> is a stable, machine-readable identifier
/// (for example <c>User.DuplicateEmail</c>); <see cref="Message"/> is for humans.
/// </summary>
/// <remarks>
/// Not sealed: the application layer derives a validation error that carries several field errors.
/// </remarks>
public record Error
{
    /// <summary>The absence of an error. Carried by every successful <see cref="Result"/>.</summary>
    public static readonly Error None = new();

    public Error(string code, string message, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (type == ErrorType.None)
        {
            throw new ArgumentException($"{nameof(ErrorType.None)} is reserved for {nameof(Error)}.{nameof(None)}.", nameof(type));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown error type.");
        }

        Code = code;
        Message = message;
        Type = type;
    }

    private Error()
    {
        Code = string.Empty;
        Message = string.Empty;
        Type = ErrorType.None;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}
