using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetBoxPlus.Testing;
using Npgsql;

namespace Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// The retrying open in TestSupport, against a forwarder that loses connections the way Docker
/// Desktop's port forwarding sometimes does: accepted, and never handed on.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ContainerConnectionsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task AConnectionLostOnTheWay_IsAbandonedAndTriedAgain()
    {
        var server = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        await using var forwarder = new LosingForwarder(server.Host!, server.Port, connectionsToLose: 1);
        var connectionString = ContainerConnections.ConnectionString(new NpgsqlConnectionStringBuilder(server.ConnectionString)
        {
            Host = IPAddress.Loopback.ToString(),
            Port = forwarder.Port,
            Pooling = false,
        }.ConnectionString);

        var services = new ServiceCollection();
        services.AddDbContext<EmptyContext>(options => options.UseNpgsql(connectionString));
        services.RetryLostConnectionAttempts<EmptyContext>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EmptyContext>();
        var elapsed = Stopwatch.StartNew();

        var answer = await context.Database.SqlQueryRaw<int>("SELECT 42 AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(42, answer);
        Assert.Equal(2, forwarder.Accepted);
        // One short attempt lost, not Npgsql's default 15 s, which is what a lost connection used to cost.
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(ContainerConnections.OpenTimeoutSeconds), TimeSpan.FromSeconds(ContainerConnections.OpenTimeoutSeconds + 5));
    }

    private sealed class EmptyContext(DbContextOptions<EmptyContext> options) : DbContext(options);

    /// <summary>Accepts connections; holds the first few without a word, forwards the rest to the server.</summary>
    private sealed class LosingForwarder : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<TcpClient> _clients = [];
        private readonly Task _accepting;
        private int _accepted;

        public LosingForwarder(string host, int port, int connectionsToLose)
        {
            _listener.Start();
            _accepting = AcceptAsync(host, port, connectionsToLose);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public int Accepted => Volatile.Read(ref _accepted);

        private async Task AcceptAsync(string host, int port, int connectionsToLose)
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_clients)
                    {
                        _clients.Add(client);
                    }

                    if (Interlocked.Increment(ref _accepted) > connectionsToLose)
                    {
                        _ = ForwardAsync(client, host, port);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ForwardAsync(TcpClient client, string host, int port)
        {
            try
            {
                using var server = new TcpClient();
                await server.ConnectAsync(host, port, _stop.Token);
                var fromClient = client.GetStream().CopyToAsync(server.GetStream(), _stop.Token);
                var fromServer = server.GetStream().CopyToAsync(client.GetStream(), _stop.Token);
                await Task.WhenAny(fromClient, fromServer);
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _accepting;
            lock (_clients)
            {
                _clients.ForEach(client => client.Dispose());
            }

            _stop.Dispose();
        }
    }
}
