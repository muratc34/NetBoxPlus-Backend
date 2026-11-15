namespace Auth.Contracts;

/// <summary>
/// Published once per account, when its e-mail address is first proven (a verification link, or a
/// password reset link). Carries the address, so a consumer can act on it even before it has seen
/// <see cref="UserRegistered"/>. Consumers find it by its full type name; never rename or move it.
/// </summary>
/// <param name="Replayed">True when the auth migrator publishes it again for a consumer that started later.</param>
public sealed record EmailVerified(Guid UserId, string Email, DateTimeOffset VerifiedAt, DateTimeOffset OccurredAt, bool Replayed = false);
