namespace Auth.Contracts;

/// <summary>
/// Published when a user needs an e-mail verification link: at registration and when they ask for a
/// new one. <paramref name="Token"/> is the raw single-use value for the link (the auth service keeps
/// only its hash); it expires at <paramref name="ExpiresAt"/>. Consumers find it by its full type
/// name; never rename or move it.
/// </summary>
/// <param name="TokenId">Unique per token: a consumer sends one e-mail per token id.</param>
public sealed record EmailVerificationRequested(Guid UserId, Guid TokenId, string Token, DateTimeOffset ExpiresAt, DateTimeOffset OccurredAt);
