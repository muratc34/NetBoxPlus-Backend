using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Shared.Infrastructure.API.Hosting;

namespace Shared.Infrastructure.API.Tests;

/// <summary>
/// Every service's own meter is exported without registering it anywhere, as long as its name
/// starts with "NetBoxPlus."; a process without HTTP gets the same through AddWorkerDefaults.
/// </summary>
public sealed class TelemetryTests
{
    [Fact]
    public async Task AService_ExportsEveryNetBoxPlusMeter_AndNoOthers()
    {
        await using var app = await TestApp.StartAsync(_ => { }, configure: builder => WithReader(builder.Services));
        _ = app.Services.GetRequiredService<MeterProvider>();

        Assert.True(IsListenedTo("NetBoxPlus.Probe" + Guid.NewGuid().ToString("N")));
        Assert.False(IsListenedTo("Elsewhere.Probe" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void AWorker_GetsTheSameTelemetry_WithoutHttp()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = Environments.Production });
        builder.AddWorkerDefaults();
        WithReader(builder.Services);
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService<OpenTelemetry.Trace.TracerProvider>());
        _ = host.Services.GetRequiredService<MeterProvider>();
        Assert.True(IsListenedTo("NetBoxPlus.Probe" + Guid.NewGuid().ToString("N")));
        Assert.False(IsListenedTo("Microsoft.AspNetCore.Hosting"));
        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
        Assert.Null(host.Services.GetService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>());
    }

    // Without a reader OpenTelemetry enables nothing; in a running service the OTLP exporter is one.
    private static void WithReader(IServiceCollection services)
        => services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddReader(new PeriodicExportingMetricReader(new DiscardingExporter())));

    // OpenTelemetry enables an instrument only on a meter it was told to listen to.
    private static bool IsListenedTo(string meterName)
    {
        using var meter = new Meter(meterName);
        return meter.CreateCounter<long>("probe").Enabled;
    }

    private sealed class DiscardingExporter : BaseExporter<Metric>
    {
        public override ExportResult Export(in Batch<Metric> batch) => ExportResult.Success;
    }
}
