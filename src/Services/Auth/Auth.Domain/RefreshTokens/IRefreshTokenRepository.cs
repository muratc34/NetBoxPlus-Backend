namespace Auth.Domain.RefreshTokens;

/// <summary>
/// Stores refresh tokens. The methods that change existing tokens run in the database at once and
/// atomically, outside the command's save: they must hold even when the command fails (a reused
/// token is rejected, and its family must still be revoked), and concurrent requests must not both
/// win.
/// </summary>
public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken);

    void Add(RefreshToken token);

    /// <summary>
    /// Marks the token used if it is still unused and not revoked.
    /// </summary>
    /// <returns><see langword="false"/> when another request used or revoked it first.</returns>
    Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes every token of the family that is not revoked yet.</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Revokes every token of the user, for example after a password change.</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
}
