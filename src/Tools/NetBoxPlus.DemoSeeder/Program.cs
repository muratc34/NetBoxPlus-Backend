using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetBoxPlus.DemoSeeder;
using NetBoxPlus.DemoSeeder.Gateway;
using NetBoxPlus.DemoSeeder.Media;
using NetBoxPlus.DemoSeeder.Seeding;

// Fills a running system with demo content through the gateway, as the admin panel would: no
// database access, no service code. Safe to run again; it finishes what is missing and changes
// nothing else. Settings come from the Seeder section (Seeder__* environment variables).
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<SeederOptions>()
    .BindConfiguration(SeederOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.AdminEmail) && !string.IsNullOrWhiteSpace(options.AdminPassword), "Seeder:AdminEmail and Seeder:AdminPassword are required.")
    .Validate(options => (options.PublicStorageEndpoint is null) == (options.StorageEndpoint is null), "Set both Seeder:PublicStorageEndpoint and Seeder:StorageEndpoint, or neither.")
    .ValidateOnStart();
builder.Services.AddSingleton(provider => provider.GetRequiredService<IOptions<SeederOptions>>().Value);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<SeederOptions>();
    return new StorageAddresses(options.PublicStorageEndpoint, options.StorageEndpoint);
});
// One client for the whole run: it holds the admin session. A typed client would be a new one, signed out, per resolution.
builder.Services.AddHttpClient(nameof(GatewayClient), (provider, http) => http.BaseAddress = provider.GetRequiredService<SeederOptions>().Gateway);
builder.Services.AddHttpClient(nameof(BlobUploader), http => http.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddSingleton(provider => new GatewayClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GatewayClient))));
builder.Services.AddSingleton(provider => new BlobUploader(
    provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(BlobUploader)),
    provider.GetRequiredService<StorageAddresses>()));
builder.Services.AddSingleton<MediaFactory>();
builder.Services.AddTransient<CatalogSeeder>();
builder.Services.AddTransient<PlanSeeder>();
builder.Logging.AddFilter("System.Net.Http", LogLevel.Warning);

using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("NetBoxPlus.DemoSeeder");
var cancellation = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;

try
{
    var options = host.Services.GetRequiredService<SeederOptions>();
    var gateway = host.Services.GetRequiredService<GatewayClient>();
    await gateway.SignInAsync(options.AdminEmail, options.AdminPassword, cancellation);
    SeederLog.SignedIn(logger, options.Gateway);

    var plans = host.Services.GetRequiredService<PlanSeeder>();
    await plans.SeedAsync(cancellation);

    var catalog = host.Services.GetRequiredService<CatalogSeeder>();
    await catalog.SeedAsync(cancellation);

    var (genres, people, movies, series) = catalog.Counts;
    SeederLog.Done(logger, genres, people, movies, series, catalog.Changes + plans.Changes);
    return 0;
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    SeederLog.Failed(logger, exception);
    return 1;
}
