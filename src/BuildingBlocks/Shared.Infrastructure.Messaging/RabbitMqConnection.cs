using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Shared.Infrastructure.Messaging;

/// <summary>
/// The service's single broker connection, opened on first use. The client recovers it
/// automatically after network failures, including declared topology and consumers.
/// </summary>
internal sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { } connection)
        {
            return connection;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is null)
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(options.Value.ConnectionString),
                    ClientProvidedName = options.Value.ServiceName,
                    AutomaticRecoveryEnabled = true,
                    TopologyRecoveryEnabled = true,
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _lock.Dispose();
    }
}
