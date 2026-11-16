namespace Auth.Contracts;

/// <summary>
/// Published when an account moved to a new, verified address. <paramref name="Revision"/> goes up with
/// every change, starting at 1 for the address in <see cref="UserRegistered"/>: messages can arrive out
/// of order, so a consumer ignores one whose revision is not higher than the last it applied for the
/// user. Consumers find it by its full type name; never rename or move it.
/// </summary>
public sealed record EmailChanged(Guid UserId, string Email, int Revision, DateTimeOffset ChangedAt, DateTimeOffset OccurredAt);
