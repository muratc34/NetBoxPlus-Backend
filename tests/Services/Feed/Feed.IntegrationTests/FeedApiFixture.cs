using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Feed.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Shared.Application.Messaging;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Feed.IntegrationTests;

/// <summary>
/// The feed API in memory, consuming from a real RabbitMQ container and writing to a real
/// PostgreSQL container. Events are published through the same transport the sample service's
/// outbox uses. Without Docker the tests are skipped.
/// </summary>
public sealed class FeedApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RabbitMqContainer? _rabbit;
    private FeedApiFactory? _factory;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
            _rabbit = new RabbitMqBuilder("rabbitmq:4.4-management-alpine").Build();
            await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        }
        catch (Exception exception) when (exception is DockerUnavailableException || exception.InnerException is DockerUnavailableException)
        {
            SkipReason = $"Docker is not available: {exception.Message}";
            return;
        }

        _factory = new FeedApiFactory(_postgres.GetConnectionString(), _rabbit.GetConnectionString());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FeedDbContext>().Database.MigrateAsync();
        }

        // Creating the client starts the host, and with it the consumer that declares the queue.
        _factory.CreateClient().Dispose();
        IsAvailable = true;
    }

    public HttpClient CreateClient()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);
        return _factory!.CreateClient();
    }

    public AsyncServiceScope CreateScope() => _factory!.Services.CreateAsyncScope();

    public FakeLogCollector Logs => _factory!.Services.GetFakeLogCollector();

    /// <summary>Publishes an event as the sample service would, with the given message id.</summary>
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, Guid messageId)
        where TEvent : class
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var envelope = new MessageEnvelope(
            messageId,
            IntegrationEvents.MessageTypeOf(typeof(TEvent)),
            DateTimeOffset.UtcNow,
            TraceParent: null,
            JsonSerializer.Serialize(integrationEvent, IntegrationEvents.SerializerOptions));

        await _factory!.Services.GetRequiredService<IMessageTransport>().SendAsync(envelope, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        if (_rabbit is not null)
        {
            await _rabbit.DisposeAsync();
        }
    }

    private sealed class FeedApiFactory(string database, string rabbit) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("Messaging:ConnectionString", rabbit);

            // The consumer reports skipped duplicates at debug level.
            builder.UseSetting("Logging:LogLevel:Shared.Infrastructure.Messaging", "Debug");
            builder.ConfigureLogging(logging => logging.AddFakeLogging());
        }
    }
}

[CollectionDefinition(nameof(FeedApiCollection))]
public sealed class FeedApiCollection : ICollectionFixture<FeedApiFixture>;
