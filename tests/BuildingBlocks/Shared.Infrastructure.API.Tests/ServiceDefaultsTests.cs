using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Logging;

namespace Shared.Infrastructure.API.Tests;

public sealed partial class ServiceDefaultsTests
{
    [Fact]
    public async Task Liveness_IsHealthyWithoutDependencies()
    {
        await using var app = await TestApp.StartAsync(_ => { });

        var response = await app.Client.GetAsync(ServiceDefaultsExtensions.LivenessPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Readiness_RunsOnlyReadyChecks()
    {
        await using var app = await TestApp.StartAsync(_ => { }, configure: builder => builder.Services.AddHealthChecks()
            .AddCheck("database", () => HealthCheckResult.Unhealthy(), tags: [HealthTags.Ready]));

        var ready = await app.Client.GetAsync(ServiceDefaultsExtensions.ReadinessPath, TestContext.Current.CancellationToken);
        var live = await app.Client.GetAsync(ServiceDefaultsExtensions.LivenessPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task OpenTelemetry_TracingAndMetricsAreRegistered()
    {
        await using var app = await TestApp.StartAsync(_ => { });

        Assert.NotNull(app.Services.GetService<TracerProvider>());
        Assert.NotNull(app.Services.GetService<MeterProvider>());
    }

    [Fact]
    public async Task ClassifiedLogParameters_AreRedacted()
    {
        await using var app = await TestApp.StartAsync(_ => { });
        var logger = app.Services.GetRequiredService<ILogger<ServiceDefaultsTests>>();

        LogSignIn(logger, "user@example.com", "s3cr3t", "web");

        var record = app.Services.GetFakeLogCollector().GetSnapshot().Single(r => r.Category == typeof(ServiceDefaultsTests).FullName);
        Assert.DoesNotContain("user@example.com", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", record.Message, StringComparison.Ordinal);
        Assert.Contains("web", record.Message, StringComparison.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {Email} signed in with {Password} from {Client}")]
    private static partial void LogSignIn(ILogger logger, [PersonalData] string email, [SecretData] string password, string client);
}
