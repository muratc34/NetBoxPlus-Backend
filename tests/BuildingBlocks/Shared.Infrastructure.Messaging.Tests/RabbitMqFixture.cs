using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetBoxPlus.Testing;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Shared.Application;
using Shared.Application.Messaging;
using Testcontainers.RabbitMq;

namespace Shared.Infrastructure.Messaging.Tests;

/// <summary>
/// One RabbitMQ container for the run. Each test uses its own service name, so its own queues.
/// Without Docker the integration tests are skipped, not failed.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private RabbitMqContainer? _container;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public string ConnectionString => _container!.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            _container = new RabbitMqBuilder("rabbitmq:4.4-management-alpine").Build();
            await _container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Starts a consumer for a fresh service name and waits until its queue is bound, so messages
    /// published afterwards are not lost.
    /// </summary>
    internal async Task<MessagingHost> StartHostAsync()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var serviceName = $"test-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:ConnectionString"] = ConnectionString,
                ["Messaging:ServiceName"] = serviceName,
                ["Messaging:RetryDelays:0"] = "00:00:00",
                ["Messaging:RetryDelays:1"] = "00:00:00",
            })
            .Build());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<Deliveries>();
        services.AddSingleton<IInbox, InMemoryInbox>();
        services.AddCqrs(typeof(RabbitMqFixture).Assembly);
        services.AddRabbitMqMessaging();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(TestContext.Current.CancellationToken);
        }

        var host = new MessagingHost(provider, hostedServices, serviceName, ConnectionString);
        await host.WaitForConsumerAsync();
        return host;
    }
}

[CollectionDefinition(nameof(RabbitMqCollection))]
public sealed class RabbitMqCollection : ICollectionFixture<RabbitMqFixture>;

internal sealed class MessagingHost(
    ServiceProvider provider,
    IReadOnlyList<IHostedService> hostedServices,
    string serviceName,
    string connectionString) : IAsyncDisposable
{
    public Deliveries Deliveries => provider.GetRequiredService<Deliveries>();

    public string Queue => RabbitMqTopology.QueueFor(serviceName);

    public string DeadLetterQueue => RabbitMqTopology.DeadLetterQueueFor(serviceName);

    public Task PublishAsync<TEvent>(TEvent integrationEvent, Guid? messageId = null, string? traceParent = null)
        where TEvent : class
        => provider.GetRequiredService<IMessageTransport>().SendAsync(
            new MessageEnvelope(
                messageId ?? Guid.CreateVersion7(),
                IntegrationEvents.MessageTypeOf(typeof(TEvent)),
                DateTimeOffset.UtcNow,
                traceParent,
                JsonSerializer.Serialize(integrationEvent, IntegrationEvents.SerializerOptions)),
            TestContext.Current.CancellationToken);

    /// <summary>Publishes a body that is not valid JSON, bypassing the transport.</summary>
    public async Task PublishRawAsync(string messageType, string body)
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.BasicPublishAsync(
            RabbitMqTopology.ExchangeFor(messageType),
            messageType,
            mandatory: false,
            new BasicProperties { MessageId = Guid.CreateVersion7().ToString(), Type = messageType },
            System.Text.Encoding.UTF8.GetBytes(body));
    }

    public async Task<uint> WaitForMessagesAsync(string queue, uint count, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        await using var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        uint current;
        while ((current = await channel.MessageCountAsync(queue)) < count)
        {
            await Task.Delay(50, cancellation.Token);
        }

        return current;
    }

    public async Task WaitForConsumerAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var connection = await new ConnectionFactory { Uri = new Uri(connectionString) }.CreateConnectionAsync();

        while (true)
        {
            try
            {
                await using var channel = await connection.CreateChannelAsync();
                if (await channel.ConsumerCountAsync(Queue) > 0)
                {
                    return;
                }
            }
            catch (OperationInterruptedException)
            {
                // The queue does not exist yet: the consumer is still declaring its topology.
            }

            await Task.Delay(50, cancellation.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        await provider.DisposeAsync();
    }
}
