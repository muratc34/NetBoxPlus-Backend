using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace NetBoxPlus.Testing;

/// <summary>
/// Opens a test's own connection to a RabbitMQ test container so that a connection lost on the way
/// does not fail the test. Linked into every test project that talks to a RabbitMQ container itself.
/// </summary>
/// <remarks>
/// The same loss as in <see cref="ContainerConnections"/>: Docker Desktop's port forwarding accepts the
/// TCP connection and never hands it to the broker, so the client waits for the broker's first frame
/// and fails with <see cref="BrokerUnreachableException"/> after 10 s. That wait ignores both the
/// cancellation token and <c>HandshakeContinuationTimeout</c>, so an attempt is abandoned here after
/// <see cref="OpenTimeout"/> (a connection that still opens later is closed) and the next one starts:
/// nothing has reached the broker yet. The service's own connection needs no help: its consumer and
/// outbox try again by themselves.
/// </remarks>
internal static class BrokerConnections
{
    /// <summary>How long one attempt to connect and to finish the handshake may take.</summary>
    public static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(5);

    private const int Attempts = 3;

    public static async Task<IConnection> OpenAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString), RequestedConnectionTimeout = OpenTimeout };

        for (var attempt = 1; ; attempt++)
        {
            using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var opening = factory.CreateConnectionAsync(abandon.Token);
            var failure = "it took longer than " + OpenTimeout.TotalSeconds + " s";
            if (await Task.WhenAny(opening, Task.Delay(OpenTimeout, cancellationToken)) == opening)
            {
                try
                {
                    return await opening;
                }
                catch (BrokerUnreachableException exception) when (attempt < Attempts && !cancellationToken.IsCancellationRequested)
                {
                    failure = exception.InnerException?.Message ?? exception.Message;
                }
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt == Attempts)
                {
                    throw new TimeoutException($"Connecting to the broker took longer than {OpenTimeout.TotalSeconds} s in each of {Attempts} attempts.");
                }

                await abandon.CancelAsync();
                _ = opening.ContinueWith(
                    late => late.Result.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion,
                    TaskScheduler.Default);
            }

            TestContext.Current.TestOutputHelper?.WriteLine($"Connecting to the broker failed (attempt {attempt} of {Attempts}: {failure}); trying again.");
        }
    }
}
