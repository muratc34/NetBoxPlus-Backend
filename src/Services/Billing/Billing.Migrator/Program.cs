using Billing.Application;
using Billing.Application.Subscriptions.AnnounceSubscriptions;
using Billing.Infrastructure;
using Billing.Infrastructure.Persistence;
using Billing.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Application.Cqrs;

// Applies pending migrations and exits. With the argument "announce-subscriptions" it also publishes
// every subscription's state, for a consumer that started after they changed. Runs as its own step before the API starts, so schema
// changes never race between API replicas and a deploy can fail before any traffic reaches the new version.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets(typeof(MigratorLog).Assembly, optional: true);

var connectionString = builder.Configuration.GetConnectionString(BillingDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{BillingDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

// The same registrations as the API, so an announcement goes through the command pipeline and into
// the outbox, which the API sends. Hosted services never start here.
builder.Services.AddBillingApplication();
builder.Services.AddBillingPersistence(connectionString);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Billing.Migrator");

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

if (args.Contains("announce-subscriptions", StringComparer.Ordinal))
{
    var announced = await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(new AnnounceSubscriptionsCommand());
    if (announced.IsFailure)
    {
        MigratorLog.AnnounceFailed(logger, announced.Error.Code);
        return 1;
    }

    MigratorLog.Announced(logger, announced.Value);
}

return 0;
