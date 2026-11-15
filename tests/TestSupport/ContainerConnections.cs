using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace NetBoxPlus.Testing;

/// <summary>
/// Connects to a PostgreSQL test container so that a connection lost on the way does not fail the
/// test. Linked into every test project that runs EF Core against a PostgreSQL container.
/// </summary>
/// <remarks>
/// Under load, Docker Desktop's port forwarding (Windows, WSL 2) now and then accepts a connection
/// to a container's mapped port and never hands it to the container: the TCP handshake with the
/// forwarder succeeds, PostgreSQL never sees the connection (its connection log stays silent), and
/// Npgsql waits out its whole <c>Timeout</c> (15 s by default) for the answer to its first message.
/// Such an attempt never recovers, so a longer timeout would only make the test wait longer.
/// Instead an open gets <see cref="OpenTimeoutSeconds"/> (a healthy one takes well under a second
/// even on a loaded machine), and one that times out is tried again. That is safe: nothing has
/// reached the database yet. Npgsql's own timeout is the limit because it is the only one that
/// also covers waiting for the server's first answer; a cancellation token does not.
/// </remarks>
internal static class ContainerConnections
{
    /// <summary>How long one attempt to open a connection may take.</summary>
    public const int OpenTimeoutSeconds = 5;

    private const int Attempts = 3;

    /// <summary>The container's connection string with the short open timeout, and another database if given.</summary>
    public static string ConnectionString(string containerConnectionString, string? database = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(containerConnectionString) { Timeout = OpenTimeoutSeconds };
        if (database is not null)
        {
            builder.Database = database;
        }

        return builder.ConnectionString;
    }

    /// <summary>Retries timed-out opens of <typeparamref name="TContext"/>, on top of how the service configures it.</summary>
    public static IServiceCollection RetryLostConnectionAttempts<TContext>(this IServiceCollection services)
        where TContext : DbContext
        => services.ConfigureDbContext<TContext>(options => options.AddInterceptors(RetryingOpen.Instance));

    private sealed class RetryingOpen : DbConnectionInterceptor
    {
        public static readonly RetryingOpen Instance = new();

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
            => result.IsSuppressed ? result : OpenAsync(connection, CancellationToken.None).GetAwaiter().GetResult();

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
            => result.IsSuppressed ? result : await OpenAsync(connection, cancellationToken);

        /// <summary>Opens the connection itself, so EF Core is told to skip its own open.</summary>
        private static async Task<InterceptionResult> OpenAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await connection.OpenAsync(cancellationToken);
                    return InterceptionResult.Suppress();
                }
                catch (Exception exception) when (
                    exception is TimeoutException || exception.InnerException is TimeoutException)
                {
                    if (attempt == Attempts || cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    TestContext.Current.TestOutputHelper?.WriteLine(
                        $"Opening a connection to {connection.DataSource} timed out (attempt {attempt} of {Attempts}: "
                        + $"{exception.Message}); trying again.");                }
            }
        }
    }
}
