using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using OpenTelemetry.Trace;
using Shared.Application;
using Shared.Application.Events;
using Shared.Application.Auditing;
using Shared.Application.Messaging;
using Shared.Infrastructure.Persistence.Auditing;
using Shared.Infrastructure.Persistence.Inbox;
using Shared.Infrastructure.Persistence.Interceptors;
using Shared.Infrastructure.Persistence.Outbox;
using Shared.Infrastructure.Persistence.Retention;

namespace Shared.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Same value as <c>HealthTags.Ready</c> in Shared.Infrastructure.API, which this project does not reference.</summary>
    private const string ReadyTag = "ready";

    /// <summary>
    /// The most connections one process keeps to its database unless the connection string says
    /// otherwise. All services share one PostgreSQL server, so the pools together must stay below
    /// its <c>max_connections</c>; Npgsql's own default of 100 lets a single busy service take every
    /// slot. A request that finds the pool full waits up to the connection string's <c>Timeout</c>.
    /// </summary>
    public const int DefaultMaxPoolSize = 20;

    private const string MaxPoolSizeKeyword = "Maximum Pool Size";

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on PostgreSQL with the shared interceptors
    /// (domain events, soft delete, auditing — in that order), the unit of work, the outbox
    /// publisher, processor and monitor (metrics, and a degraded health check while messages are given up on), the inbox, the clean-up of old outbox and inbox rows, the audit log, a readiness health check and Npgsql tracing.
    /// The connection pool is limited to <see cref="DefaultMaxPoolSize"/> unless the connection
    /// string sets <c>Maximum Pool Size</c>, and a connection that cannot be had (pool exhausted,
    /// server unreachable or out of connection slots) fails with <see cref="DataUnavailableException"/>.
    /// </summary>
    /// <remarks>Requires <c>AddCqrs</c> for the domain event dispatcher.</remarks>
    public static IServiceCollection AddPersistence<TContext>(
        this IServiceCollection services,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configureNpgsql = null)
        where TContext : ServiceDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<DomainEventsInterceptor>();
        services.TryAddScoped<SoftDeleteInterceptor>();
        services.TryAddScoped<AuditingInterceptor>();
        services.TryAddSingleton<ConnectionFailureInterceptor>();

        var pooledConnectionString = WithPoolLimit(connectionString);
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(pooledConnectionString, configureNpgsql)
            .AddInterceptors(
                provider.GetRequiredService<DomainEventsInterceptor>(),
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditingInterceptor>(),
                provider.GetRequiredService<ConnectionFailureInterceptor>()));

        services.TryAddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        services.TryAddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher<TContext>>();
        services.TryAddScoped<IInbox, EfInbox<TContext>>();
        services.TryAddScoped<IAuditLog, EfAuditLog<TContext>>();

        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .Validate(o => o.BatchSize > 0 && o.MaxAttempts > 0 && o.PollingInterval > TimeSpan.Zero && o.MonitorInterval > TimeSpan.Zero, "Outbox settings must be positive.")
            .ValidateOnStart();
        services.AddMetrics();
        services.TryAddSingleton<OutboxMetrics>();
        services.AddHostedService<OutboxProcessor<TContext>>();
        services.AddHostedService<OutboxMonitor<TContext>>();

        services.AddOptions<MessageRetentionOptions>()
            .BindConfiguration(MessageRetentionOptions.SectionName)
            .Validate(o => o.IsValid(), "Message retention settings must be positive.")
            .ValidateOnStart();
        services.AddHostedService<MessageRetentionService<TContext>>();

        services.AddHealthChecks()
            .AddDbContextCheck<TContext>("database", tags: [ReadyTag])
            .AddCheck<OutboxHealthCheck>("outbox", tags: [ReadyTag]);
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddNpgsql());

        return services;
    }

    /// <summary>The connection string with <see cref="DefaultMaxPoolSize"/>, unless it sets a pool size of its own.</summary>
    internal static string WithPoolLimit(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // ContainsKey is true for every keyword Npgsql knows; Keys lists only those that were set,
        // under their canonical name whichever synonym was used.
        var setsPoolSize = builder.Keys.Cast<string>()
            .Any(key => string.Equals(key, MaxPoolSizeKeyword, StringComparison.OrdinalIgnoreCase));
        if (setsPoolSize)
        {
            return connectionString;
        }

        builder.MaxPoolSize = DefaultMaxPoolSize;
        return builder.ConnectionString;
    }
}
