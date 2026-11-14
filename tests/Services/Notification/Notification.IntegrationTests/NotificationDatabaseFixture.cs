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

    /// <param name="settings">On top of the defaults: an SMTP server that is not there, and the web app at app.netboxplus.test.</param>
    public async Task<ServiceProvider> CreateServicesAsync(IReadOnlyDictionary<string, string?>? settings = null)
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var connectionString = new NpgsqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            Database = $"notification_{Guid.NewGuid():N}",
        }.ConnectionString;

        var services = new ServiceCollection();
        var values = new Dictionary<string, string?>
        {
            ["Outbox:Enabled"] = "false",
            ["Email:From"] = "NetBoxPlus <no-reply@netboxplus.test>",
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = "1",
            ["Notification:WebBaseUrl"] = "https://app.netboxplus.test",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddNotificationApplication();
        services.AddNotificationPersistence(connectionString);
        services.AddEmail(configuration);
        services.AddEmailSending();

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
