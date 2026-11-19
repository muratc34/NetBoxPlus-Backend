using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Gateway.Sessions;

/// <summary>
/// One connection to Valkey for the process, opened on first use: the gateway starts, and serves
/// routes without sessions, while Valkey is still coming up.
/// </summary>
internal sealed class ValkeyConnection(string connectionString) : IDisposable
{
    private readonly Lazy<ConnectionMultiplexer> _connection = new(() =>
    {
        var options = ConfigurationOptions.Parse(connectionString);

        // Keep retrying in the background instead of failing the first caller for good.
        options.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(options);
    });

    public IDatabase Database => _connection.Value.GetDatabase();

    public void Dispose()
    {
        if (_connection.IsValueCreated)
        {
            _connection.Value.Dispose();
        }
    }
}

/// <summary>Ready only while Valkey answers: without it no session can be read.</summary>
internal sealed class ValkeyHealthCheck(ValkeyConnection valkey) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var latency = await valkey.Database.PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy($"PING in {latency.TotalMilliseconds:0} ms");
        }
        catch (RedisException exception)
        {
            return HealthCheckResult.Unhealthy("Valkey does not answer.", exception);
        }
    }
}
