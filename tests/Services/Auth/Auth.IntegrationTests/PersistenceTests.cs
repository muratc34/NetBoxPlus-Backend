using Auth.Domain.RefreshTokens;
using Auth.Domain.Roles;
using Auth.Domain.Users;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Auth.IntegrationTests;

[Collection(nameof(AuthDatabaseCollection))]
public sealed class PersistenceTests(AuthDatabaseFixture database)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_IsFoundByEmailWithItsRoles()
    {
        await using var services = await database.CreateServicesAsync();
        var role = Role.Create("Editor", [Permissions.Auth.UsersRead]).Value;
        var user = User.Register(Email("jane@example.com"), "hash");
        user.AssignRole(role.Id);
        await SaveAsync(services, context => context.AddRange(role, user));

        await using var scope = services.CreateAsyncScope();
        var found = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByEmailAsync(Email(" JANE@example.com "), Cancellation);

        Assert.NotNull(found);
        Assert.Equal(user.Id, found.Id);
        Assert.Equal(role.Id, Assert.Single(found.Roles).RoleId);
        Assert.NotEqual(default, found.CreatedAt);
    }

    [Fact]
    public async Task SameEmailTwice_IsRejectedByTheDatabase()
    {
        await using var services = await database.CreateServicesAsync();
        await SaveAsync(services, context => context.Add(User.Register(Email("jane@example.com"), "hash")));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(services, context => context.Add(User.Register(Email("Jane@Example.com"), "hash"))));
    }

    [Fact]
    public async Task RoleNamesDifferingOnlyInCase_AreRejectedByTheDatabase()
    {
        await using var services = await database.CreateServicesAsync();
        await SaveAsync(services, context => context.Add(Role.Create("Editor", []).Value));

        await using (var scope = services.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<IRoleRepository>().ExistsByNameAsync("EDITOR", Cancellation));
        }

        await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(services, context => context.Add(Role.Create("editor", []).Value)));
    }

    [Fact]
    public async Task Permissions_RoundTrip()
    {
        await using var services = await database.CreateServicesAsync();
        var role = Role.Create("Editor", [Permissions.Sample.NotesDelete, Permissions.Auth.UsersRead]).Value;
        await SaveAsync(services, context => context.Add(role));

        await using var scope = services.CreateAsyncScope();
        var found = await scope.ServiceProvider.GetRequiredService<IRoleRepository>().GetByIdAsync(role.Id, Cancellation);

        Assert.Equal([Permissions.Auth.UsersRead, Permissions.Sample.NotesDelete], found!.Permissions);
    }

    [Fact]
    public async Task DeletingARole_TakesItAwayFromUsers()
    {
        await using var services = await database.CreateServicesAsync();
        var role = Role.Create("Editor", []).Value;
        var user = User.Register(Email("jane@example.com"), "hash");
        user.AssignRole(role.Id);
        await SaveAsync(services, context => context.AddRange(role, user));

        await using (var scope = services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
            roles.Remove((await roles.GetByIdAsync(role.Id, Cancellation))!);
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().SaveChangesAsync(Cancellation);
        }

        await using var check = services.CreateAsyncScope();
        var found = await check.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(user.Id, Cancellation);
        Assert.Empty(found!.Roles);
    }

    [Fact]
    public async Task ConcurrentFailedSignIns_AreAllCounted()
    {
        await using var services = await database.CreateServicesAsync();
        var user = User.Register(Email("jane@example.com"), "hash");
        await SaveAsync(services, context => context.Add(user));

        // Seven at once: the fifth locks the account and starts the count again, so a lost update
        // anywhere shows up as a wrong count or a missing lockout.
        await Task.WhenAll(Enumerable.Range(0, 7).Select(async _ =>
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().RecordFailedSignInAsync(user.Id, Now, Cancellation);
        }));

        var stored = await LoadUserAsync(services, user.Id);
        Assert.Equal(2, stored.FailedSignInCount);
        Assert.Equal(Now + User.LockoutDuration, stored.LockedOutUntil);
        Assert.Equal(UserErrors.LockedOut, stored.CanSignIn(Now).Error);
        Assert.True(stored.CanSignIn(Now + User.LockoutDuration).IsSuccess);
    }

    [Fact]
    public async Task FailedSignInsBelowTheLimit_DoNotLock()
    {
        await using var services = await database.CreateServicesAsync();
        var user = User.Register(Email("jane@example.com"), "hash");
        await SaveAsync(services, context => context.Add(user));

        for (var attempt = 1; attempt < User.MaxFailedSignIns; attempt++)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IUserRepository>().RecordFailedSignInAsync(user.Id, Now, Cancellation);
        }

        var stored = await LoadUserAsync(services, user.Id);
        Assert.Equal(User.MaxFailedSignIns - 1, stored.FailedSignInCount);
        Assert.Null(stored.LockedOutUntil);
    }

    [Fact]
    public async Task ConcurrentUsesOfOneRefreshToken_ExactlyOneWins()
    {
        await using var services = await database.CreateServicesAsync();
        var token = await SaveTokenAsync(services, SessionKind.Customer);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>().TryMarkUsedAsync(token.Id, Now, Cancellation);
        }));

        Assert.Single(results, won => won);
        var stored = await LoadTokenAsync(services, token.TokenHash);
        Assert.Equal(Now, stored.UsedAt);
        Assert.True(stored.IsSpent);
    }

    [Fact]
    public async Task RevokedToken_CannotBeUsed()
    {
        await using var services = await database.CreateServicesAsync();
        var token = await SaveTokenAsync(services, SessionKind.Customer);

        await using var scope = services.CreateAsyncScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
        await tokens.RevokeFamilyAsync(token.FamilyId, Now, Cancellation);

        Assert.False(await tokens.TryMarkUsedAsync(token.Id, Now, Cancellation));
    }

    [Fact]
    public async Task RevokeFamily_LeavesOtherFamiliesAlone()
    {
        await using var services = await database.CreateServicesAsync();
        var user = User.Register(Email("jane@example.com"), "hash");
        var first = RefreshToken.Issue(user.Id, SessionKind.Customer, Hash(1), Now);
        var rotated = first.Rotate(Hash(2), Now);
        var other = RefreshToken.Issue(user.Id, SessionKind.Admin, Hash(3), Now);
        await SaveAsync(services, context => context.AddRange(user, first, rotated, other));

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>().RevokeFamilyAsync(first.FamilyId, Now, Cancellation);
        }

        Assert.Equal(Now, (await LoadTokenAsync(services, first.TokenHash)).RevokedAt);
        Assert.Equal(Now, (await LoadTokenAsync(services, rotated.TokenHash)).RevokedAt);
        Assert.Null((await LoadTokenAsync(services, other.TokenHash)).RevokedAt);
    }

    [Fact]
    public async Task RevokeAllForUser_RevokesEveryFamilyOfThatUserOnly()
    {
        await using var services = await database.CreateServicesAsync();
        var jane = User.Register(Email("jane@example.com"), "hash");
        var john = User.Register(Email("john@example.com"), "hash");
        var janeCustomer = RefreshToken.Issue(jane.Id, SessionKind.Customer, Hash(1), Now);
        var janeAdmin = RefreshToken.Issue(jane.Id, SessionKind.Admin, Hash(2), Now);
        var johns = RefreshToken.Issue(john.Id, SessionKind.Customer, Hash(3), Now);
        await SaveAsync(services, context => context.AddRange(jane, john, janeCustomer, janeAdmin, johns));

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>().RevokeAllForUserAsync(jane.Id, Now, Cancellation);
        }

        Assert.NotNull((await LoadTokenAsync(services, janeCustomer.TokenHash)).RevokedAt);
        Assert.NotNull((await LoadTokenAsync(services, janeAdmin.TokenHash)).RevokedAt);
        Assert.Null((await LoadTokenAsync(services, johns.TokenHash)).RevokedAt);
    }

    [Fact]
    public async Task SameTokenHashTwice_IsRejectedByTheDatabase()
    {
        await using var services = await database.CreateServicesAsync();
        var user = User.Register(Email("jane@example.com"), "hash");
        await SaveAsync(services, context => context.AddRange(user, RefreshToken.Issue(user.Id, SessionKind.Customer, Hash(1), Now)));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(services, context => context.Add(RefreshToken.Issue(user.Id, SessionKind.Customer, Hash(1), Now))));
    }

    private static async Task SaveAsync(ServiceProvider services, Action<AuthDbContext> change)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        change(context);
        await context.SaveChangesAsync(Cancellation);
    }

    private static async Task<RefreshToken> SaveTokenAsync(ServiceProvider services, SessionKind kind)
    {
        var user = User.Register(Email($"user-{Guid.NewGuid():N}@example.com"), "hash");
        var token = RefreshToken.Issue(user.Id, kind, Hash(1), Now);
        await SaveAsync(services, context => context.AddRange(user, token));
        return token;
    }

    private static async Task<User> LoadUserAsync(ServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(id, Cancellation))!;
    }

    private static async Task<RefreshToken> LoadTokenAsync(ServiceProvider services, byte[] hash)
    {
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>().GetByHashAsync(hash, Cancellation))!;
    }

    private static EmailAddress Email(string value) => EmailAddress.Create(value).Value;

    private static byte[] Hash(byte seed) => Enumerable.Repeat(seed, RefreshToken.HashLength).ToArray();
}
