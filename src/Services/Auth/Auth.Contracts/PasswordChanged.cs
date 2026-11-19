namespace Auth.Contracts;

/// <summary>
/// Published when a user's password changed, by the user or with a reset link, so the user can be
/// told. Consumers find it by its full type name; never rename or move it.
/// </summary>
/// <param name="ChangeId">Unique per change: a consumer sends one notice per change id.</param>
public sealed record PasswordChanged(Guid UserId, Guid ChangeId, DateTimeOffset ChangedAt, DateTimeOffset OccurredAt);
