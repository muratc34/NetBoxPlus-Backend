using System.Net.Http.Headers;
using System.Security.Cryptography;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Sample.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Sample.IntegrationTests;

/// <summary>
/// The sample API in memory, against real PostgreSQL and RabbitMQ containers, with the schema
/// created by the service's own migrations. Without Docker the tests are skipped.
/// </summary>
public sealed class SampleApiFixture : IAsyncLifetime
{
    public const string Issuer = "https://auth.test";
    public const string Audience = "netboxplus";

    private readonly RSA _signingKey = RSA.Create(2048);
    private PostgreSqlContainer? _postgres;
    private RabbitMqContainer? _rabbit;
    private SampleApiFactory? _factory;

    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public string RabbitConnectionString => _rabbit!.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
            _rabbit = new RabbitMqBuilder("rabbitmq:4.4-management-alpine").Build();
            await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());
        }
        catch (Exception exception) when (exception is DockerUnavailableException || exception.InnerException is DockerUnavailableException)
        {
            SkipReason = $"Docker is not available: {exception.Message}";
            return;
        }

        _factory = new SampleApiFactory(_postgres.GetConnectionString(), _rabbit.GetConnectionString(), _signingKey);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database.MigrateAsync();

        IsAvailable = true;
    }

    public HttpClient CreateClient(string? accessToken = null)
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var client = _factory!.CreateClient();
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return client;
    }

    public AsyncServiceScope CreateScope() => _factory!.Services.CreateAsyncScope();

    public string CreateToken(params string[] permissions) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        Audience = Audience,
        Expires = DateTime.UtcNow.AddMinutes(15),
        Claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = Guid.CreateVersion7().ToString(),
            ["permission"] = permissions,
        },
        SigningCredentials = new SigningCredentials(new RsaSecurityKey(_signingKey), SecurityAlgorithms.RsaSha256),
    });

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

        if (_rabbit is not null)
        {
            await _rabbit.DisposeAsync();
        }

        _signingKey.Dispose();
    }

    private sealed class SampleApiFactory(string database, string rabbit, RSA signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("Messaging:ConnectionString", rabbit);
            builder.UseSetting("Outbox:PollingInterval", "00:00:00.200");

            // Stands in for the Auth service's published keys.
            builder.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.ValidIssuer = Issuer;
                    options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(signingKey);
                }));
        }
    }
}

[CollectionDefinition(nameof(SampleApiCollection))]
public sealed class SampleApiCollection : ICollectionFixture<SampleApiFixture>;
