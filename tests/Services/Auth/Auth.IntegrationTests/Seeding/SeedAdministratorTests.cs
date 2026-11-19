using Auth.Application.Users.SeedAdministrator;
using Auth.Domain.Roles;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Application.Validation;
using Shared.Domain;

namespace Auth.IntegrationTests.Seeding;

[Collection(nameof(AuthDatabaseCollection))]
public sealed class SeedAdministratorTests(AuthDatabaseFixture database)
{
    private const string Email = "admin@example.com";
    private const string Password = "correct horse battery staple";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FirstRun_CreatesTheRoleAndTheAccountWithIt()
    {
        await using var services = await database.CreateServicesAsync();

        var outcome = await SeedAsync(services, Email, Password);

        Assert.Equal(new SeedAdministratorOutcome(RoleChanged: true, UserCreated: true, RoleAssigned: true), outcome.Value);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var role = await context.Roles.SingleAsync(Cancellation);
        var user = await context.Users.Include(user => user.Roles).SingleAsync(Cancellation);

        Assert.True(role.IsSystem);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), role.Permissions);
        Assert.Equal(Email, user.Email.Value);
        Assert.Equal(role.Id, Assert.Single(user.Roles).RoleId);

        // The account is an ordinary registration as far as other services are concerned.
        Assert.Contains(
            await context.OutboxMessages.Select(message => message.Type).ToListAsync(Cancellation),
            type => type == IntegrationEvents.MessageTypeOf(typeof(Auth.Contracts.UserRegistered)));

        var entries = await context.AuditLogEntries.OrderBy(entry => entry.Action).ToListAsync(Cancellation);
        Assert.Equal(["SeedAdministrator", "SeedAdministratorRole"], entries.Select(entry => entry.Action));
        Assert.All(entries, entry => Assert.Null(entry.ActorId));
        Assert.Equal(user.Id, entries[0].TargetId);
        Assert.DoesNotContain(Password, entries[0].Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecondRun_ChangesNothing()
    {
        await using var services = await database.CreateServicesAsync();
        await SeedAsync(services, Email, Password);

        var outcome = await SeedAsync(services, Email, Password);

        Assert.Equal(new SeedAdministratorOutcome(false, false, false), outcome.Value);
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AuthDbContext>().AuditLogEntries.CountAsync(Cancellation));
    }

    [Fact]
    public async Task ExistingAccount_KeepsItsPasswordAndGetsTheRoleBack()
    {
        await using var services = await database.CreateServicesAsync();
        await SeedAsync(services, Email, Password);
        string hashBefore;

        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var user = await context.Users.Include(user => user.Roles).SingleAsync(Cancellation);
            hashBefore = user.PasswordHash;
            user.RemoveRole(user.Roles.Single().RoleId);
            await context.SaveChangesAsync(Cancellation);
        }

        var outcome = await SeedAsync(services, Email, "a different password, also long");

        Assert.Equal(new SeedAdministratorOutcome(false, false, true), outcome.Value);
        await using (var scope = services.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Users.Include(user => user.Roles).SingleAsync(Cancellation);
            Assert.Equal(hashBefore, user.PasswordHash);
            Assert.Single(user.Roles);
        }
    }

    [Fact]
    public async Task WithoutAnAccount_OnlyTheRoleIsSeeded()
    {
        await using var services = await database.CreateServicesAsync();

        var outcome = await SeedAsync(services, null, null);

        Assert.Equal(new SeedAdministratorOutcome(true, false, false), outcome.Value);
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        Assert.Equal(1, await context.Roles.CountAsync(Cancellation));
        Assert.Equal(0, await context.Users.CountAsync(Cancellation));
    }

    [Fact]
    public async Task RoleBehindTheCatalog_GetsTheNewPermissions()
    {
        await using var services = await database.CreateServicesAsync();
        await SeedAsync(services, null, null);

        // As if the catalog had grown since the last deploy.
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.ExecuteSqlAsync(
                $"UPDATE \"Roles\" SET \"Permissions\" = ARRAY[{Permissions.Auth.UsersRead}]",
                Cancellation);
        }

        var outcome = await SeedAsync(services, null, null);

        Assert.True(outcome.Value.RoleChanged);
        await using (var scope = services.CreateAsyncScope())
        {
            var role = await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Roles.SingleAsync(Cancellation);
            Assert.Equal(Permissions.All.Count, role.Permissions.Count);
        }
    }

    [Theory]
    [InlineData(Email, null, "Seed.Incomplete")]
    [InlineData(null, Password, "Seed.Incomplete")]
    [InlineData("not-an-address", Password, "User.EmailInvalid")]
    [InlineData(Email, "short", "User.PasswordTooShort")]
    public async Task InvalidSettings_SeedNothing(string? email, string? password, string code)
    {
        await using var services = await database.CreateServicesAsync();

        var outcome = await SeedAsync(services, email, password);

        var error = Assert.IsType<ValidationError>(outcome.Error);
        Assert.Contains(code, error.Failures.Select(failure => failure.Code));
        await using var scope = services.CreateAsyncScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Roles.CountAsync(Cancellation));
    }

    private static async Task<Result<SeedAdministratorOutcome>> SeedAsync(ServiceProvider services, string? email, string? password)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .Send(new SeedAdministratorCommand(email, password), Cancellation);
    }
}
