using System.Net;
using System.Net.Sockets;
using MessagingTests.Contracts;
using NetBoxPlus.Testing;
using Shared.Application.Messaging;
using Testcontainers.RabbitMq;

namespace Shared.Infrastructure.Messaging.Tests;

/// <summary>
/// A broker that goes away and comes back, as in a restart. It has a container of its own, on a
/// fixed port: a restarted container would otherwise come back on another one.
/// </summary>
public sealed class BrokerRestartTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task WhileTheBrokerIsDown_SendingSaysSo_AndAfterItReturns_MessagesFlowAgain()
    {
        var port = FreePort();
        var container = new RabbitMqBuilder("rabbitmq:4.4-management-alpine").WithPortBinding(port, 5672).Build();
        try
        {
            await container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            Assert.Skip($"Docker is not available: {exception.Message}");
        }

        await using (container)
        {
            await using var host = await RabbitMqFixture.StartHostAsync(container.GetConnectionString());
            await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 1));
            await host.Deliveries.WaitForAsync(1, Timeout);

            await container.StopAsync(TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<MessageTransportUnavailableException>(() => host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 2)));

            await container.StartAsync(TestContext.Current.CancellationToken);

            // What the outbox does: keep trying while the broker is unavailable.
            using var cancellation = new CancellationTokenSource(Timeout);
            while (true)
            {
                try
                {
                    await host.PublishAsync(new OrderPlaced(Guid.CreateVersion7(), 3));
                    break;
                }
                catch (MessageTransportUnavailableException)
                {
                    await Task.Delay(500, cancellation.Token);
                }
            }

            await host.Deliveries.WaitForAsync(2, Timeout);
            Assert.Contains(host.Deliveries.Received, delivery => delivery.Event is OrderPlaced { Total: 3 });
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
