using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Logging;

namespace Shared.Infrastructure.API.Hosting;

/// <summary>
/// The setup every NetBoxPlus ASP.NET Core application shares: telemetry, health checks, log
/// redaction and RFC 9457 error responses.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public const string LivenessPath = "/health";
    public const string ReadinessPath = "/health/ready";

    /// <summary>
    /// Registers OpenTelemetry (traces, metrics, logs; exported over OTLP when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set), health checks, log redaction, JSON console logs
    /// outside development and problem-details error handling.
    /// </summary>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton(TimeProvider.System);

        ConfigureLogging(builder);
        ConfigureOpenTelemetry(builder);

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [HealthTags.Live]);

        builder.Services.AddErrorResponses();

        return builder;
    }

    /// <summary>
    /// Adds the exception handler and problem-details status pages. Call first, so that it wraps
    /// the rest of the pipeline.
    /// </summary>
    public static IApplicationBuilder UseServiceDefaults(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }

    /// <summary>
    /// Maps <c>/health</c> (liveness: the process is up) and <c>/health/ready</c> (readiness: the
    /// checks tagged <see cref="HealthTags.Ready"/> pass).
    /// </summary>
    public static IEndpointRouteBuilder MapDefaultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Live),
        });

        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
        });

        return endpoints;
    }

    private static void ConfigureLogging(IHostApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment())
        {
            builder.Logging.AddJsonConsole(options =>
            {
                options.IncludeScopes = true;
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "O";
            });
        }

        builder.Services.AddRedaction(redaction => redaction.SetRedactor<ErasingRedactor>(
            new DataClassificationSet(DataClassifications.Personal),
            new DataClassificationSet(DataClassifications.Secret)));

        builder.Logging.EnableRedaction();
    }

    private static void ConfigureOpenTelemetry(IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealthRequest(context))
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }
    }

    private static bool IsHealthRequest(HttpContext context)
        => context.Request.Path.StartsWithSegments(LivenessPath, StringComparison.OrdinalIgnoreCase);
}
