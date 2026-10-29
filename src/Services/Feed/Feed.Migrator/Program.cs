using Feed.Infrastructure.Persistence;
using Feed.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Applies pending migrations and exits. Runs as its own step before the API starts, so schema changes
// never race between API replicas and a deploy can fail before any traffic reaches the new version.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets(typeof(MigratorLog).Assembly, optional: true);

var connectionString = builder.Configuration.GetConnectionString(FeedDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{FeedDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

builder.Services.AddDbContext<FeedDbContext>(options => options.UseNpgsql(connectionString));

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var context = scope.ServiceProvider.GetRequiredService<FeedDbContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Feed.Migrator");

var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
if (pending.Count == 0)
{
    MigratorLog.UpToDate(logger);
    return;
}

MigratorLog.Applying(logger, pending.Count, string.Join(", ", pending));
await context.Database.MigrateAsync();
MigratorLog.Applied(logger, pending.Count);
