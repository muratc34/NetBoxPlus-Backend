using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shared.Application;
using Shared.Infrastructure.Persistence.Interceptors;

namespace Shared.Infrastructure.Persistence.Tests;

/// <summary>
/// Every service shares one PostgreSQL server: the pool is capped, and a connection that cannot be
/// had for lack of room or reach fails as <see cref="DataUnavailableException"/> (503), nothing else.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ConnectionBudgetTests(PostgresFixture postgres)
{
    private const string RolePassword = "role-password";

    [Fact]
    public void ConnectionStringWithoutPoolSize_GetsTheDefaultLimit()
    {
        var limited = new NpgsqlConnectionStringBuilder(
            PersistenceServiceCollectionExtensions.WithPoolLimit("Host=db;Database=catalog;Username=catalog"));

        Assert.Equal(PersistenceServiceCollectionExtensions.DefaultMaxPoolSize, limited.MaxPoolSize);
        Assert.Equal("catalog", limited.Database);
    }

    [Theory]
    [InlineData("Host=db;Maximum Pool Size=7")]
    [InlineData("Host=db;MaxPoolSize=7")]
    [InlineData("Host=db;maximum pool size=7")]
    public void PoolSizeInTheConnectionString_IsKept(string connectionString)
        => Assert.Equal(7, new NpgsqlConnectionStringBuilder(PersistenceServiceCollectionExtensions.WithPoolLimit(connectionString)).MaxPoolSize);

    [Fact]
    public async Task ServiceContext_UsesTheLimitedPool()
    {
        await using var services = await postgres.CreateServicesAsync(createSchema: false);
        await using var scope = services.Scope();

        var connectionString = scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.GetConnectionString();

        Assert.Equal(PersistenceServiceCollectionExtensions.DefaultMaxPoolSize, new NpgsqlConnectionStringBuilder(connectionString).MaxPoolSize);
    }

    [Fact]
    public async Task ExhaustedPool_IsDataUnavailable()
    {
        await using var services = await postgres.CreateServicesAsync(connection: builder =>
        {
            builder.MaxPoolSize = 1;
            builder.Timeout = 1;
        });

        await using var holder = services.Scope();
        await holder.ServiceProvider.GetRequiredService<TestDbContext>().Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

        await using var waiter = services.Scope();
        var failure = await Assert.ThrowsAsync<DataUnavailableException>(
            () => waiter.ServiceProvider.GetRequiredService<TestDbContext>().Products.CountAsync(TestContext.Current.CancellationToken));

        Assert.IsType<NpgsqlException>(failure.InnerException);
    }

    [Fact]
    public async Task ServerOutOfConnectionSlots_IsDataUnavailable()
    {
        // The role's limit stands in for max_connections: both are refused with 53300 before the
        // database is looked at.
        var role = await CreateRoleAsync("CONNECTION LIMIT 0");
        await using var services = await postgres.CreateServicesAsync(createSchema: false, connection: builder =>
        {
            builder.Username = role;
            builder.Password = RolePassword;
        });
        await using var scope = services.Scope();

        var failure = await Assert.ThrowsAsync<DataUnavailableException>(
            () => scope.ServiceProvider.GetRequiredService<TestDbContext>().Products.CountAsync(TestContext.Current.CancellationToken));

        Assert.Equal(PostgresErrorCodes.TooManyConnections, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    [Fact]
    public async Task WrongPassword_IsNotDataUnavailable()
    {
        var role = await CreateRoleAsync();
        await using var services = await postgres.CreateServicesAsync(createSchema: false, connection: builder =>
        {
            builder.Username = role;
            builder.Password = "wrong-password";
        });
        await using var scope = services.Scope();

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => scope.ServiceProvider.GetRequiredService<TestDbContext>().Products.CountAsync(TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Chain(failure), exception => exception is DataUnavailableException);
        Assert.Contains(Chain(failure), exception => exception is PostgresException { SqlState: PostgresErrorCodes.InvalidPassword });
    }

    [Fact]
    public async Task CommandTimeout_IsNotDataUnavailable()
    {
        // Npgsql calls a timed-out command transient too, but the connection was there: that is a
        // slow query to look into, not a full pool.
        await using var services = await postgres.CreateServicesAsync(connection: builder => builder.CommandTimeout = 1);
        await using var scope = services.Scope();

        var failure = await Assert.ThrowsAnyAsync<Exception>(
            () => scope.ServiceProvider.GetRequiredService<TestDbContext>().Database
                .ExecuteSqlRawAsync("SELECT pg_sleep(3)", TestContext.Current.CancellationToken));

        Assert.DoesNotContain(Chain(failure), exception => exception is DataUnavailableException);
        Assert.Contains(Chain(failure), exception => exception is NpgsqlException);
    }

    public static TheoryData<string, bool> ServerErrors => new()
    {
        { PostgresErrorCodes.TooManyConnections, true },
        { PostgresErrorCodes.InsufficientResources, true },
        { PostgresErrorCodes.OutOfMemory, true },
        { PostgresErrorCodes.ConnectionFailure, true },
        { PostgresErrorCodes.CannotConnectNow, true },
        { PostgresErrorCodes.InvalidPassword, false },
        { PostgresErrorCodes.InvalidCatalogName, false },
        { PostgresErrorCodes.AdminShutdown, false },
    };

    [Theory]
    [MemberData(nameof(ServerErrors))]
    public void ServerRefusal_IsUnavailableOnlyForCapacityAndReach(string sqlState, bool unavailable)
        => Assert.Equal(unavailable, ConnectionFailureInterceptor.IsUnavailable(new PostgresException("refused", "FATAL", "FATAL", sqlState)));

    [Fact]
    public void CanceledOpen_IsNotUnavailable()
        => Assert.False(ConnectionFailureInterceptor.IsUnavailable(new OperationCanceledException()));

    [Fact]
    public void TransientClientFailure_IsUnavailable()
        => Assert.True(ConnectionFailureInterceptor.IsUnavailable(new NpgsqlException("Failed to connect.", new TimeoutException())));

    private async Task<string> CreateRoleAsync(string options = "")
    {
        var role = $"role_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE ROLE {role} LOGIN PASSWORD '{RolePassword}' {options}", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return role;
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
