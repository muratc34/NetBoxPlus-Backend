using System.Net.Http.Headers;
using System.Security.Cryptography;
using Catalog.Infrastructure.Images;
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
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Azurite;
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
    private AzuriteContainer? _azurite;
    private IContainer? _elasticsearch;
    private CatalogApiFactory? _factory;
    private string _images = string.Empty;
    private string _database = string.Empty;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
            _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0")
                .WithCommand("--skipApiVersionCheck")
                .Build();
            _elasticsearch = new ContainerBuilder("elasticsearch:9.5.5")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "false")
                .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(9200, assignRandomHostPort: true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9200).ForPath("/_cluster/health")))
                .Build();
            await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync(), _elasticsearch.StartAsync());
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
            return;
        }

        await ImageStorageSetup.EnsureAsync(_azurite.GetConnectionString(), []);
        _images = _azurite.GetConnectionString();
        _database = _postgres.GetConnectionString();
        _factory = new CatalogApiFactory(_database, _images, $"http://127.0.0.1:{_elasticsearch.GetMappedPublicPort(9200)}", _signingKey);

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

    /// <summary>The same API with a search engine that nothing answers at.</summary>
    public HttpClient CreateClientWithoutSearch()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);
        return new CatalogApiFactory(_database, _images, "http://127.0.0.1:1", _signingKey).CreateClient();
    }

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

        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }

        if (_elasticsearch is not null)
        {
            await _elasticsearch.DisposeAsync();
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

    private sealed class CatalogApiFactory(string database, string images, string search, ECDsa signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("ConnectionStrings:Images", images);
            builder.UseSetting("ConnectionStrings:Search", search);
            builder.UseSetting("Messaging:ConnectionString", "amqp://localhost:1/");

            builder.ConfigureTestServices(services =>
            {
                var workers = services
                    .Where(service => service.ServiceType == typeof(IHostedService)
                        && (service.ImplementationType?.Namespace?.StartsWith("Shared.Infrastructure.", StringComparison.Ordinal) == true
                            || service.ImplementationType == typeof(Catalog.Infrastructure.Search.SearchIndexingService)))
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
