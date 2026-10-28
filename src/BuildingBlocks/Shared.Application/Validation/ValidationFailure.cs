namespace Shared.Application.Validation;

/// <summary>One invalid field of a request.</summary>
/// <param name="PropertyName">The request property the failure belongs to.</param>
/// <param name="Code">Stable, machine-readable identifier, for example <c>Register.EmailRequired</c>.</param>
/// <param name="Message">Explanation for humans.</param>
public sealed record ValidationFailure(string PropertyName, string Code, string Message);
