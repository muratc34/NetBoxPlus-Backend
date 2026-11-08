using Catalog.Infrastructure;
using Catalog.Infrastructure.Images;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Search;
using Catalog.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Applies pending migrations, prepares image storage and the search index, and exits. With the
// argument "reindex" it also rebuilds the search index from the database. Runs as its own step before the API starts, so schema changes
// never race between API replicas and a deploy can fail before any traffic reaches the new version.
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets(typeof(MigratorLog).Assembly, optional: true);

var connectionString = builder.Configuration.GetConnectionString(CatalogDbContext.ConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{CatalogDbContext.ConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

builder.Services.AddDbContext<CatalogDbContext>(options => options.UseNpgsql(connectionString));

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Catalog.Migrator");

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

var imagesConnectionString = builder.Configuration.GetConnectionString(CatalogInfrastructureServiceCollectionExtensions.ImagesConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{CatalogInfrastructureServiceCollectionExtensions.ImagesConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

await ImageStorageSetup.EnsureAsync(imagesConnectionString, builder.Configuration.GetSection("Images:UploadOrigins").Get<string[]>() ?? []);
MigratorLog.ImagesReady(logger);

var searchUrl = builder.Configuration.GetConnectionString(CatalogInfrastructureServiceCollectionExtensions.SearchConnectionStringName)
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:{CatalogInfrastructureServiceCollectionExtensions.SearchConnectionStringName} is not configured. Locally, run ./deploy/dev.ps1 secrets.");

if (args.Contains("reindex", StringComparer.Ordinal))
{
    MigratorLog.Reindexed(logger, await SearchMaintenance.ReindexAsync(context, searchUrl, TimeProvider.System));
}
else if (await SearchMaintenance.TryEnsureIndexAsync(searchUrl))
{
    MigratorLog.SearchReady(logger);
}
else
{
    // Search is optional locally: the catalog works without it, and changes wait in the queue.
    MigratorLog.SearchUnreachable(logger);
}
