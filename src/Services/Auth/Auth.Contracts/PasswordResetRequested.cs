namespace Auth.Contracts;

/// <summary>
/// Published when a user asks to reset a forgotten password. <paramref name="Token"/> is the raw
/// single-use value for the link (the auth service keeps only its hash); it expires at
/// <paramref name="ExpiresAt"/>. Consumers find it by its full type name; never rename or move it.
/// </summary>
/// <param name="TokenId">Unique per token: a consumer sends one e-mail per token id.</param>
public sealed record PasswordResetRequested(Guid UserId, Guid TokenId, string Token, DateTimeOffset ExpiresAt, DateTimeOffset OccurredAt);
