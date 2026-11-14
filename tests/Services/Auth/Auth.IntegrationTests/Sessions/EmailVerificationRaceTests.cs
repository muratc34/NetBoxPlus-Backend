using System.Security.Cryptography;
using System.Text;
using Auth.Application.EmailVerification.ConfirmEmail;
using Auth.Domain.Users;
using Auth.Domain.UserTokens;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;

namespace Auth.IntegrationTests.Sessions;

/// <summary>
/// Using a token is decided in the database: these tests make the race happen on purpose instead of
/// hoping concurrent requests overlap.
/// </summary>
[Collection(nameof(AuthDatabaseCollection))]
public sealed class EmailVerificationRaceTests(AuthDatabaseFixture database)
{
    private const string Value = "the-token-value";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TryMarkUsed_WinsOnce_AndNeverOnARevokedToken()
    {
        await using var services = await database.CreateServicesAsync();
        var (_, used) = await SeedAsync(services);
        var (_, revoked) = await SeedAsync(services, revoke: true);

        await using var scope = services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IUserTokenRepository>();
        var now = DateTimeOffset.UtcNow;

        Assert.True(await tokens.TryMarkUsedAsync(used, now, Cancellation));
        Assert.False(await tokens.TryMarkUsedAsync(used, now, Cancellation));
        Assert.False(await tokens.TryMarkUsedAsync(revoked, now, Cancellation));
    }

    [Fact]
    public async Task ATokenUsedBetweenReadingAndUsingIt_DoesNotVerify()
    {
        await using var services = await database.CreateServicesAsync(collection =>
        {
            collection.AddScoped<UserTokenRepositoryProbe>();
            collection.AddScoped<IUserTokenRepository>(provider => provider.GetRequiredService<UserTokenRepositoryProbe>());
        });
        var (userId, _) = await SeedAsync(services);

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new ConfirmEmailCommand(Value), Cancellation);

        Assert.Equal("EmailVerification.TokenInvalid", result.Error.Code);
        await using var check = services.CreateAsyncScope();
        var user = await check.ServiceProvider.GetRequiredService<AuthDbContext>().Users.AsNoTracking().SingleAsync(item => item.Id == userId, Cancellation);
        Assert.False(user.IsEmailVerified);
    }

    private static async Task<(Guid UserId, Guid TokenId)> SeedAsync(ServiceProvider services, bool revoke = false)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = User.Register(EmailAddress.Create($"user-{Guid.NewGuid():N}@example.com").Value, "hash");
        var value = revoke ? Value + Guid.NewGuid().ToString("N") : Value;
        var token = UserToken.IssueEmailVerification(user.Id, value, SHA256.HashData(Encoding.UTF8.GetBytes(value)), DateTimeOffset.UtcNow);
        if (revoke)
        {
            token.Revoke(DateTimeOffset.UtcNow);
        }

        user.ClearDomainEvents();
        token.ClearDomainEvents();
        context.AddRange(user, token);
        await context.SaveChangesAsync(Cancellation);
        return (user.Id, token.Id);
    }

    /// <summary>
    /// The real repository, except that another request uses the token right after it is read: the
    /// moment two concurrent confirmations collide.
    /// </summary>
    private sealed class UserTokenRepositoryProbe(AuthDbContext context, IServiceScopeFactory scopes) : IUserTokenRepository
    {
        private readonly UserTokenRepository _inner = new(context);

        public async Task<UserToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken)
        {
            var token = await _inner.GetByHashAsync(tokenHash, cancellationToken);
            if (token is not null)
            {
                await using var other = scopes.CreateAsyncScope();
                await new UserTokenRepository(other.ServiceProvider.GetRequiredService<AuthDbContext>()).TryMarkUsedAsync(token.Id, DateTimeOffset.UtcNow, cancellationToken);
            }

            return token;
        }

        public Task<IReadOnlyList<UserToken>> GetUnusedAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken)
            => _inner.GetUnusedAsync(userId, purpose, cancellationToken);

        public Task<DateTimeOffset?> GetLastIssuedAtAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken)
            => _inner.GetLastIssuedAtAsync(userId, purpose, cancellationToken);

        public void Add(UserToken token) => _inner.Add(token);

        public Task<bool> TryMarkUsedAsync(Guid tokenId, DateTimeOffset now, CancellationToken cancellationToken)
            => _inner.TryMarkUsedAsync(tokenId, now, cancellationToken);
    }
}
