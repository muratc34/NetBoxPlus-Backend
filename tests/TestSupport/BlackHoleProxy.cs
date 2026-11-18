using System.Net;
using System.Net.Sockets;

namespace NetBoxPlus.Testing;

/// <summary>
/// A TCP proxy in front of a container's port that does what Docker Desktop's port forwarding does
/// now and then under load: it accepts the first <c>swallowed</c> connections and never passes them on,
/// so the client waits for an answer that does not come. Later connections reach the container.
/// Tests use it to show that the connection helpers survive such a loss.
/// </summary>
internal sealed class BlackHoleProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<TcpClient> _held = [];
    private readonly Task _accepting;
    private readonly string _targetHost;
    private readonly int _targetPort;
    private int _toSwallow;
    private int _swallowed;

    public BlackHoleProxy(string targetHost, int targetPort, int swallowed)
    {
        _targetHost = targetHost;
        _targetPort = targetPort;
        _toSwallow = swallowed;
        _listener.Start();
        _accepting = AcceptAsync();
    }

    /// <summary>The port clients connect to instead of the container's.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>How many connections were accepted and never passed on.</summary>
    public int Swallowed => Volatile.Read(ref _swallowed);

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        try
        {
            await _accepting;
        }
        catch (OperationCanceledException)
        {
        }

        lock (_held)
        {
            _held.ForEach(client => client.Dispose());
        }

        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_held)
            {
                _held.Add(client);
            }

            if (Interlocked.Decrement(ref _toSwallow) >= 0)
            {
                Interlocked.Increment(ref _swallowed);
                continue;
            }

            _ = ForwardAsync(client);
        }
    }

    private async Task ForwardAsync(TcpClient client)
    {
        try
        {
            using var target = new TcpClient();
            await target.ConnectAsync(_targetHost, _targetPort, _stopping.Token);
            lock (_held)
            {
                _held.Add(target);
            }

            var inbound = client.GetStream();
            var outbound = target.GetStream();
            await Task.WhenAny(inbound.CopyToAsync(outbound, _stopping.Token), outbound.CopyToAsync(inbound, _stopping.Token));
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // The client or the container went away, or the proxy is stopping.
        }
        finally
        {
            client.Dispose();
        }
    }
}
