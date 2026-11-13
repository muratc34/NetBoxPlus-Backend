using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetBoxPlus.Testing;
using Notification.Application;
using Notification.Infrastructure;
using Notification.Infrastructure.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Notification.IntegrationTests;

/// <summary>
/// One PostgreSQL container for the test run; every test gets its own database with the notification
/// service's migrations applied. Without Docker the tests are skipped.
/// </summary>
public sealed class NotificationDatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
        }
    }

    public async Task<ServiceProvider> CreateServicesAsync()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var connectionString = new NpgsqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            Database = $"notification_{Guid.NewGuid():N}",
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Outbox:Enabled"] = "false" })
            .Build());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddNotificationApplication();
        services.AddNotificationPersistence(connectionString);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        return provider;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition(nameof(NotificationDatabaseCollection))]
public sealed class NotificationDatabaseCollection : ICollectionFixture<NotificationDatabaseFixture>;
