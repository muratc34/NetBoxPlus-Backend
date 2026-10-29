using System.Diagnostics;
using MessagingTests.Contracts;
using Shared.Application.Messaging;

namespace Shared.Infrastructure.Messaging.Tests;

[Collection(nameof(RabbitMqCollection))]
public sealed class ConsumerTests(RabbitMqFixture rabbit)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task PublishedEvent_ReachesItsHandler()
    {
        await using var host = await rabbit.StartHostAsync();
        var order = new OrderPlaced(Guid.CreateVersion7(), 99.90m);

        await host.PublishAsync(order);
        await host.Deliveries.WaitForAsync(1, Timeout);

        Assert.Equal(order, Assert.Single(host.Deliveries.Received).Event);
    }

    [Fact]
    public async Task RedeliveredMessage_IsHandledOnce()
    {
        await using var host = await rabbit.StartHostAsync();
        var messageId = Guid.CreateVersion7();

        await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 1), messageId);
        await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 1), messageId);
        await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 2));
        await host.Deliveries.WaitForAsync(2, Timeout);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal(2, host.Deliveries.Received.Count);
    }

    [Fact]
    public async Task ThrowingHandler_IsRetriedThenDeadLettered()
    {
        await using var host = await rabbit.StartHostAsync();

        await host.PublishAsync(new ShipmentDelayed(Guid.CreateVersion7()));
        await host.WaitForMessagesAsync(host.DeadLetterQueue, 1, Timeout);

        // One attempt plus one per configured retry delay (two in tests).
        Assert.Equal(3, host.Deliveries.ShipmentAttempts);
    }

    [Fact]
    public async Task FailedResult_IsAcknowledgedNotDeadLettered()
    {
        await using var host = await rabbit.StartHostAsync();

        await host.PublishAsync(new PaymentRejected(Guid.CreateVersion7(), "card declined"));
        await host.Deliveries.WaitForAsync(1, Timeout);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        Assert.Equal(0u, await host.WaitForMessagesAsync(host.DeadLetterQueue, 0, Timeout));
        Assert.Equal(0u, await host.WaitForMessagesAsync(host.Queue, 0, Timeout));
    }

    [Fact]
    public async Task UnreadablePayload_IsDeadLetteredWithoutRetrying()
    {
        await using var host = await rabbit.StartHostAsync();

        await host.PublishRawAsync(IntegrationEvents.MessageTypeOf(typeof(OrderPlaced)), "this is not json");
        await host.WaitForMessagesAsync(host.DeadLetterQueue, 1, Timeout);

        Assert.Empty(host.Deliveries.Received);
    }

    [Fact]
    public async Task Handler_ContinuesThePublishersTrace()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("consumer-tests");
        using var request = source.StartActivity("request")!;

        await using var host = await rabbit.StartHostAsync();
        await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 5), traceParent: request.Id);
        await host.Deliveries.WaitForAsync(1, Timeout);

        Assert.Equal(request.TraceId, Assert.Single(host.Deliveries.Received).TraceId);
    }
}
