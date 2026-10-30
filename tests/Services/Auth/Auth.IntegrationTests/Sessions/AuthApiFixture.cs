using Auth.Infrastructure.Persistence;
using Auth.IntegrationTests.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetBoxPlus.Testing;
using Testcontainers.PostgreSql;

namespace Auth.IntegrationTests.Sessions;

/// <summary>
/// The auth API in memory against a real PostgreSQL container, migrated by the service's own
/// migrations. Tests share the database and keep apart by using their own e-mail addresses.
/// Without Docker the tests are skipped.
/// </summary>
public sealed class AuthApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private AuthApiFactory? _factory;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
            await _postgres.StartAsync();
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
            return;
        }

        _factory = new AuthApiFactory("test-key", ("test-key", TestSigningKeys.NewPem()))
        {
            Database = _postgres.GetConnectionString(),
        };

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();

        IsAvailable = true;
    }

    public HttpClient CreateClient()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);
        return _factory!.CreateClient();
    }

    public AsyncServiceScope CreateScope() => _factory!.Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}

[CollectionDefinition(nameof(AuthApiCollection))]
public sealed class AuthApiCollection : ICollectionFixture<AuthApiFixture>;
