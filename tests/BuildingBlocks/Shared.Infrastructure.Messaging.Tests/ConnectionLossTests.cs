using System.Diagnostics;
using NetBoxPlus.Testing;

namespace Shared.Infrastructure.Messaging.Tests;

/// <summary>A test's own broker connection survives one that the port forwarding swallows.</summary>
[Collection(nameof(RabbitMqCollection))]
public sealed class ConnectionLossTests(RabbitMqFixture broker)
{
    [Fact]
    public async Task AConnectionThatIsSwallowed_IsGivenUpEarly_AndTriedAgain()
    {
        Assert.SkipUnless(broker.IsAvailable, broker.SkipReason);
        var address = new Uri(broker.ConnectionString);
        await using var proxy = new BlackHoleProxy(address.Host, address.Port, swallowed: 1);
        var throughProxy = new UriBuilder(address) { Port = proxy.Port }.Uri.ToString();

        var watch = Stopwatch.StartNew();
        await using var connection = await BrokerConnections.OpenAsync(throughProxy, TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.True(connection.IsOpen);
        Assert.Equal(1, proxy.Swallowed);

        // The client's own handshake timeout is 10 s: the lost attempt was given up after the short one.
        Assert.InRange(watch.Elapsed, BrokerConnections.OpenTimeout - TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
    }
}
