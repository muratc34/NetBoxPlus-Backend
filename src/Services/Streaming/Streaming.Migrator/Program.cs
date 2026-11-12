using Streaming.Infrastructure;
using Streaming.Infrastructure.Media;
using Streaming.Infrastructure.Persistence;
using Streaming.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Applies pending migrations, prepares video storage and exits. Runs as its own step before the API starts, so schema
// changes never race between API replicas and a deploy can fail before any traffic reaches the new version.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets(typeof(MigratorLog).Assembly, optional: true);

var connectionString = builder.Configuration.GetConnectionString(StreamingDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{StreamingDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

builder.Services.AddDbContext<StreamingDbContext>(options => options.UseNpgsql(connectionString));

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var context = scope.ServiceProvider.GetRequiredService<StreamingDbContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Streaming.Migrator");

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

var videosConnectionString = builder.Configuration.GetConnectionString(StreamingInfrastructureServiceCollectionExtensions.VideosConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{StreamingInfrastructureServiceCollectionExtensions.VideosConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

await VideoStorageSetup.EnsureAsync(
    videosConnectionString,
    builder.Configuration.GetSection("Videos:UploadOrigins").Get<string[]>() ?? [],
    builder.Configuration.GetSection("Videos:PlaybackOrigins").Get<string[]>() ?? []);
MigratorLog.VideosReady(logger);
