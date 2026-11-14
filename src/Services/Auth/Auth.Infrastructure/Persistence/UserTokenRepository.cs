using Auth.Domain.UserTokens;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Persistence;

internal sealed class UserTokenRepository(AuthDbContext context) : IUserTokenRepository
{
    public Task<UserToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken)
        => context.UserTokens.FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<UserToken>> GetUnusedAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken)
        => await context.UserTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAt == null && token.RevokedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<DateTimeOffset?> GetLastIssuedAtAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken)
        => context.UserTokens
            .Where(token => token.UserId == userId && token.Purpose == purpose)
            .MaxAsync(token => (DateTimeOffset?)token.CreatedAt, cancellationToken);

    public void Add(UserToken token) => context.UserTokens.Add(token);

    public async Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The condition and the change are one statement: of two concurrent requests with the same
        // token, the second finds UsedAt already set and updates nothing.
        var updated = await context.UserTokens
            .Where(token => token.Id == tokenId && token.UsedAt == null && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.UsedAt, now), cancellationToken)
            .ConfigureAwait(false);

        return updated == 1;
    }
}
