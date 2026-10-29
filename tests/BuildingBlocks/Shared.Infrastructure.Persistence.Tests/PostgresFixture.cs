using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shared.Application;
using Shared.Application.Messaging;
using Testcontainers.PostgreSql;

namespace Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// One PostgreSQL container for the whole test run. Each test gets its own database, so tests stay
/// isolated and can run in parallel. Without Docker the integration tests are skipped, not failed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
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
        catch (Exception exception) when (IsDockerMissing(exception))
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

    public void SkipIfUnavailable() => Assert.SkipUnless(IsAvailable, SkipReason);

    /// <summary>A container wired like a service, on a fresh database that already has the schema.</summary>
    internal async Task<TestServices> CreateServicesAsync(Action<IServiceCollection>? configure = null)
    {
        SkipIfUnavailable();

        var connectionString = new NpgsqlConnectionStringBuilder(_container!.GetConnectionString())
        {
            Database = $"test_{Guid.NewGuid():N}",
        }.ConnectionString;

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var transport = new RecordingTransport();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Outbox:Enabled"] = "false" })
            .Build());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IMessageTransport>(transport);
        services.AddCqrs(typeof(PostgresFixture).Assembly);
        services.AddPersistence<TestDbContext>(connectionString);
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        return new TestServices(provider, time, transport);
    }

    private static bool IsDockerMissing(Exception exception)
        => exception is DockerUnavailableException
            || exception.InnerException is DockerUnavailableException
            || exception is TypeInitializationException { InnerException: DockerUnavailableException };
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

internal sealed record TestServices(ServiceProvider Provider, FakeTimeProvider Time, RecordingTransport Transport) : IAsyncDisposable
{
    public AsyncServiceScope Scope() => Provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => Provider.DisposeAsync();
}

/// <summary>Stands in for the broker: records what was sent, or fails on demand.</summary>
internal sealed class RecordingTransport : IMessageTransport
{
    private readonly List<MessageEnvelope> _sent = [];

    public IReadOnlyList<MessageEnvelope> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public Exception? FailWith { get; set; }

    /// <summary>When set, each send waits for it — used to hold a batch's lock open.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource FirstSendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task SendAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        FirstSendStarted.TrySetResult();

        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (FailWith is { } failure)
        {
            throw failure;
        }

        lock (_sent)
        {
            _sent.Add(envelope);
        }
    }
}
