using Shared.Domain;

namespace Auth.Domain.RefreshTokens;

public static class RefreshTokenErrors
{
    /// <summary>Unknown, expired, used, revoked or of a disabled account: the caller signs in again.</summary>
    public static readonly Error Invalid = Error.Unauthorized("RefreshToken.Invalid", "The refresh token is not valid. Sign in again.");
}
