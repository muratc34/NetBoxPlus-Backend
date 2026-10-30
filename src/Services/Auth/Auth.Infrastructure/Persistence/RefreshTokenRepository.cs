using Auth.Domain.RefreshTokens;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository(AuthDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken)
        => context.RefreshTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);

    public async Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The condition and the change are one statement: of two concurrent refreshes with the same
        // token, the second finds UsedAt already set and updates nothing.
        var updated = await context.RefreshTokens
            .Where(token => token.Id == tokenId && token.UsedAt == null && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAt, now), cancellationToken)
            .ConfigureAwait(false);

        return updated == 1;
    }

    public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
        => context.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);

    public Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        => context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
}
