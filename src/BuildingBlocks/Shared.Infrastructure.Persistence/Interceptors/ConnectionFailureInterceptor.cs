using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Shared.Application;

namespace Shared.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Reports a connection that could not be opened for want of capacity or reachability as a
/// <see cref="DataUnavailableException"/>, which the API answers with 503 instead of 500.
/// </summary>
/// <remarks>
/// Only the opening of a connection is looked at: then nothing has reached the database yet and
/// trying again later is safe. Covered: the pool stayed exhausted for the whole <c>Timeout</c>,
/// the server could not be reached or did not answer in time, and the server refused the
/// connection for lack of resources (class 53, such as <c>53300</c> too many connections),
/// a connection exception (class 08) or because it is starting or shutting down (<c>57P03</c>).
/// Wrong credentials, a missing database and every failure of a command stay what they are: they
/// are faults to fix, not load to wait out. EF Core rethrows what this notification throws in
/// place of the original exception, which stays the inner exception.
/// </remarks>
internal sealed class ConnectionFailureInterceptor : DbConnectionInterceptor
{
    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        ThrowIfUnavailable(eventData.Exception);
        base.ConnectionFailed(connection, eventData);
    }

    public override Task ConnectionFailedAsync(
        DbConnection connection,
        ConnectionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnavailable(eventData.Exception);
        return base.ConnectionFailedAsync(connection, eventData, cancellationToken);
    }

    internal static bool IsUnavailable(Exception exception) => exception switch
    {
        OperationCanceledException => false,
        PostgresException postgres => postgres.SqlState.StartsWith("53", StringComparison.Ordinal)
            || postgres.SqlState.StartsWith("08", StringComparison.Ordinal)
            || postgres.SqlState == PostgresErrorCodes.CannotConnectNow,
        NpgsqlException npgsql => npgsql.IsTransient,
        TimeoutException => true,
        _ => false,
    };

    private static void ThrowIfUnavailable(Exception exception)
    {
        if (IsUnavailable(exception))
        {
            throw new DataUnavailableException(exception);
        }
    }
}
