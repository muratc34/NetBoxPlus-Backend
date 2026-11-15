using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Trace;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging;

public static partial class MessagingServiceCollectionExtensions
{
    /// <summary>Same value as <c>HealthTags.Ready</c> in Shared.Infrastructure.API, which this project does not reference.</summary>
    private const string ReadyTag = "ready";

    /// <summary>
    /// Registers the RabbitMQ transport used by the outbox processor and a consumer for every
    /// integration event handler <c>AddCqrs</c> found, a dead-letter monitor (metrics, an error log and a
    /// degraded health check while the queue holds messages), readiness health checks, tracing and the counters.
    /// Settings come from the <c>Messaging</c> section.
    /// </summary>
    /// <remarks>Consuming requires an <see cref="IInbox"/>, which the persistence layer registers.</remarks>
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<RabbitMqOptions>()
            .BindConfiguration(RabbitMqOptions.SectionName)
            .PostConfigure(o =>
            {
                if (o.RetryDelays.Length == 0)
                {
                    o.RetryDelays = [.. RabbitMqOptions.DefaultRetryDelays];
                }
            })
            .Validate(o => Uri.TryCreate(o.ConnectionString, UriKind.Absolute, out _), "Messaging:ConnectionString must be an AMQP URI.")
            .Validate(o => ServiceNamePattern().IsMatch(o.ServiceName), "Messaging:ServiceName must be lower-case kebab case, for example 'notification'.")
            .Validate(o => o.PrefetchCount > 0 && o.RetryDelays.All(d => d >= TimeSpan.Zero) && o.DeadLetterCheckInterval > TimeSpan.Zero, "Messaging:PrefetchCount and DeadLetterCheckInterval must be positive and RetryDelays non-negative.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RabbitMqConnection>();
        services.TryAddSingleton<IMessageTransport, RabbitMqTransport>();
        services.AddMetrics();
        services.TryAddSingleton<MessagingMetrics>();
        services.AddHostedService<RabbitMqConsumer>();
        services.AddHostedService<DeadLetterMonitor>();

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag])
            .AddCheck<DeadLetterHealthCheck>("dead-letters", tags: [ReadyTag]);
        // The meter ("NetBoxPlus.Messaging") is exported with every NetBoxPlus meter by the service defaults.
        services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(MessagingTelemetry.SourceName));

        return services;
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex ServiceNamePattern();
}
