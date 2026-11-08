using System.Net.Http.Headers;
using System.Security.Cryptography;
using Catalog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NetBoxPlus.Testing;
using Testcontainers.PostgreSql;

namespace Catalog.IntegrationTests.Admin;

/// <summary>
/// The catalog API in memory against a real PostgreSQL container, migrated by the service's own
/// migrations; tests sign their tokens with a key of their own. Without Docker the tests are skipped.
/// </summary>
public sealed class CatalogApiFixture : IAsyncLifetime
{
    private const string Issuer = "https://auth.test";

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private PostgreSqlContainer? _postgres;
    private CatalogApiFactory? _factory;

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

        _factory = new CatalogApiFactory(_postgres.GetConnectionString(), _signingKey);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();

        IsAvailable = true;
    }

    /// <summary>A client whose token holds exactly the given permissions.</summary>
    public HttpClient CreateClient(params string[] permissions)
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(permissions));
        return client;
    }

    public HttpClient CreateAnonymousClient()
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

        _signingKey.Dispose();
    }

    private string CreateToken(string[] permissions) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        TokenType = "at+jwt",
        Issuer = Issuer,
        Audience = "netboxplus",
        Expires = DateTime.UtcNow.AddMinutes(15),
        Claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = Guid.CreateVersion7().ToString(),
            ["permission"] = permissions,
        },
        SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256),
    });

    private sealed class CatalogApiFactory(string database, ECDsa signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("Messaging:ConnectionString", "amqp://localhost:1/");

            builder.ConfigureTestServices(services =>
            {
                var workers = services
                    .Where(service => service.ServiceType == typeof(IHostedService)
                        && service.ImplementationType?.Namespace?.StartsWith("Shared.Infrastructure.", StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var worker in workers)
                {
                    services.Remove(worker);
                }

                // Stands in for the auth service's published keys.
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.IssuerSigningKey = new ECDsaSecurityKey(signingKey);
                });
            });
        }
    }
}

[CollectionDefinition(nameof(CatalogApiCollection))]
public sealed class CatalogApiCollection : ICollectionFixture<CatalogApiFixture>;
