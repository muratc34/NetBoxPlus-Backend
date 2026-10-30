namespace Auth.Contracts;

/// <summary>Published when an account is created. Consumers find it by its full type name; never rename or move it.</summary>
public sealed record UserRegistered(Guid UserId, string Email, DateTimeOffset RegisteredAt);
