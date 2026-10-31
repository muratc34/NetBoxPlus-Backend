using Auth.Application;
using Auth.Application.Users.SeedAdministrator;
using Auth.Infrastructure;
using Auth.Infrastructure.Persistence;
using Auth.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Cqrs;

// Applies pending migrations, then seeds the administrator, and exits. Runs as its own step before
// the API starts, so schema changes never race between API replicas and a deploy can fail before
// any traffic reaches the new version.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets(typeof(MigratorLog).Assembly, optional: true);

var connectionString = builder.Configuration.GetConnectionString(AuthDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{AuthDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

// The same registrations as the API, so the seed goes through the command pipeline: validation,
// one transaction, the UserRegistered event in the outbox. Hosted services never start here.
builder.Services.AddAuthApplication();
builder.Services.AddAuthPersistence(connectionString);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Auth.Migrator");

await using (var scope = host.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

    var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
    if (pending.Count == 0)
    {
        MigratorLog.UpToDate(logger);
    }
    else
    {
        MigratorLog.Applying(logger, pending.Count, string.Join(", ", pending));
        await context.Database.MigrateAsync();
        MigratorLog.Applied(logger, pending.Count);
    }
}

await using (var scope = host.Services.CreateAsyncScope())
{
    var seed = builder.Configuration.GetSection(AdministratorSeedSettings.SectionName).Get<AdministratorSeedSettings>() ?? new();
    if (string.IsNullOrEmpty(seed.AdminEmail) && string.IsNullOrEmpty(seed.AdminPassword))
    {
        MigratorLog.NoAdministratorConfigured(logger, AdministratorSeedSettings.SectionName);
    }

    var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
        .Send(new SeedAdministratorCommand(seed.AdminEmail, seed.AdminPassword));

    if (result.IsFailure)
    {
        // The code only: the message of a password policy failure is harmless, but the input is not logged.
        MigratorLog.SeedFailed(logger, result.Error.Code);
        return 1;
    }

    MigratorLog.Seeded(logger, result.Value.RoleChanged, result.Value.UserCreated, result.Value.RoleAssigned);
}

return 0;
