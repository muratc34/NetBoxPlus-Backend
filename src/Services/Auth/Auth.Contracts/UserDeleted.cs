namespace Auth.Contracts;

/// <summary>
/// Published when an account is deleted. Every service that keeps a copy of the user deletes it, and
/// what it keeps about the user's activity. Carries no personal data: a service that must still write to
/// the user once takes the address from its own copy. Consumers find it by its full type name; never
/// rename or move it.
/// </summary>
public sealed record UserDeleted(Guid UserId, DateTimeOffset DeletedAt, DateTimeOffset OccurredAt);
