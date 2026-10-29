using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging.Tests;

/// <summary>Naming, settings and tracing rules — no broker needed.</summary>
public sealed class TopologyAndOptionsTests
{
    [Theory]
    [InlineData("Billing.Contracts.SubscriptionCreated", "netboxplus.billing.events")]
    [InlineData("Catalog.Contracts.Movies.MoviePublished", "netboxplus.catalog.events")]
    public void Exchange_ComesFromThePublishingService(string messageType, string expected)
    {
        Assert.Equal(expected, RabbitMqTopology.ExchangeFor(messageType));
    }

    [Theory]
    [InlineData("SubscriptionCreated")]
    [InlineData("Billing.SubscriptionCreated")]
    [InlineData("Billing.Events.SubscriptionCreated")]
    public void Exchange_RequiresTheContractsNamespace(string messageType)
    {
        Assert.Throws<InvalidOperationException>(() => RabbitMqTopology.ExchangeFor(messageType));
    }

    [Fact]
    public void Queues_AreNamedAfterTheConsumingService()
    {
        Assert.Equal("netboxplus.notification", RabbitMqTopology.QueueFor("notification"));
        Assert.Equal("netboxplus.notification.dead-letter", RabbitMqTopology.DeadLetterQueueFor("notification"));
    }

    [Theory]
    [InlineData("Notification")]
    [InlineData("notification_service")]
    [InlineData("")]
    public void InvalidServiceName_IsRejected(string serviceName)
    {
        var options = Options(new() { ["Messaging:ConnectionString"] = "amqp://localhost", ["Messaging:ServiceName"] = serviceName });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void MissingConnectionString_IsRejected()
    {
        var options = Options(new() { ["Messaging:ServiceName"] = "notification" });

        Assert.Throws<OptionsValidationException>(() => options.Value);
    }

    [Fact]
    public void ValidSettings_AreAccepted()
    {
        var options = Options(new() { ["Messaging:ConnectionString"] = "amqp://localhost", ["Messaging:ServiceName"] = "billing-worker" });

        Assert.Equal("billing-worker", options.Value.ServiceName);
        Assert.Equal(RabbitMqOptions.DefaultRetryDelays, options.Value.RetryDelays);
    }

    [Fact]
    public void ConfiguredRetryDelays_ReplaceTheDefaults()
    {
        var options = Options(new()
        {
            ["Messaging:ConnectionString"] = "amqp://localhost",
            ["Messaging:ServiceName"] = "notification",
            ["Messaging:RetryDelays:0"] = "00:00:02",
        });

        // The binder appends to arrays that already have elements; the defaults must not be kept.
        Assert.Equal([TimeSpan.FromSeconds(2)], options.Value.RetryDelays);
    }

    [Fact]
    public void PublishSpan_ContinuesTheStoredTrace()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MessagingTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        var traceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        using var activity = MessagingTelemetry.StartPublish(
            new MessageEnvelope(Guid.CreateVersion7(), "Billing.Contracts.SubscriptionCreated", DateTimeOffset.UtcNow, traceParent, "{}"),
            "netboxplus.billing.events");

        Assert.NotNull(activity);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", activity.TraceId.ToString());
        Assert.Equal(ActivityKind.Producer, activity.Kind);
    }

    private static IOptions<RabbitMqOptions> Options(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddRabbitMqMessaging();
        return services.BuildServiceProvider().GetRequiredService<IOptions<RabbitMqOptions>>();
    }
}
