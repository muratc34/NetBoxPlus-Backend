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

namespace Shared.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Same value as <c>HealthTags.Ready</c> in Shared.Infrastructure.API, which this project does not reference.</summary>
    private const string ReadyTag = "ready";

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on PostgreSQL with the shared interceptors
    /// (domain events, soft delete, auditing — in that order), the unit of work, the outbox
    /// publisher and processor, the inbox, the audit log, a readiness health check and Npgsql tracing.
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

        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(connectionString, configureNpgsql)
            .AddInterceptors(
                provider.GetRequiredService<DomainEventsInterceptor>(),
                provider.GetRequiredService<SoftDeleteInterceptor>(),
                provider.GetRequiredService<AuditingInterceptor>()));

        services.TryAddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        services.TryAddScoped<IIntegrationEventPublisher, OutboxIntegrationEventPublisher<TContext>>();
        services.TryAddScoped<IInbox, EfInbox<TContext>>();
        services.TryAddScoped<IAuditLog, EfAuditLog<TContext>>();

        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .Validate(o => o.BatchSize > 0 && o.MaxAttempts > 0 && o.PollingInterval > TimeSpan.Zero, "Outbox settings must be positive.")
            .ValidateOnStart();
        services.AddHostedService<OutboxProcessor<TContext>>();

        services.AddHealthChecks().AddDbContextCheck<TContext>("database", tags: [ReadyTag]);
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddNpgsql());

        return services;
    }
}
