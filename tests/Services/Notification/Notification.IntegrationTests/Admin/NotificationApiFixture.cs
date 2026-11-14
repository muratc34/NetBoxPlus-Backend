using System.Net.Http.Headers;
using System.Security.Cryptography;
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
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Notification.IntegrationTests.Admin;

/// <summary>
/// The notification API in memory against a real PostgreSQL container, migrated by the service's own
/// migrations; tests sign their tokens with a key of their own. The sending worker does not run: tests
/// drive the dispatcher themselves. Without Docker the tests are skipped.
/// </summary>
public sealed class NotificationApiFixture : IAsyncLifetime
{
    private const string Issuer = "https://auth.test";

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private PostgreSqlContainer? _postgres;
    private NotificationApiFactory? _factory;

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

        _factory = new NotificationApiFactory(_postgres.GetConnectionString(), _signingKey);
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
        IsAvailable = true;
    }

    /// <summary>A client whose token holds exactly the given permissions.</summary>
    public HttpClient CreateClient(params string[] permissions)
    {
        Assert.SkipUnless(IsAvailable, SkipReason);
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(Guid.CreateVersion7(), permissions));
        return client;
    }

    public HttpClient CreateAnonymousClient()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);
        return _factory!.CreateClient();
    }

    public AsyncServiceScope CreateScope()
    {
        Assert.SkipUnless(IsAvailable, SkipReason);
        return _factory!.Services.CreateAsyncScope();
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

        _signingKey.Dispose();
    }

    private string CreateToken(Guid userId, string[] permissions) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        TokenType = "at+jwt",
        Issuer = Issuer,
        Audience = "netboxplus",
        Expires = DateTime.UtcNow.AddMinutes(15),
        Claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            ["permission"] = permissions,
        },
        SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(_signingKey), SecurityAlgorithms.EcdsaSha256),
    });

    private sealed class NotificationApiFactory(string database, ECDsa signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("Messaging:ConnectionString", "amqp://localhost:1/");
            builder.UseSetting("Email:From", "NetBoxPlus <no-reply@netboxplus.test>");
            builder.UseSetting("Email:Smtp:Host", "127.0.0.1");
            builder.UseSetting("Email:Smtp:Port", "1");
            builder.UseSetting("Notification:WebBaseUrl", "https://app.netboxplus.test");

            builder.ConfigureTestServices(services =>
            {
                var workers = services
                    .Where(service => service.ServiceType == typeof(IHostedService)
                        && (service.ImplementationType?.Namespace?.StartsWith("Shared.Infrastructure.", StringComparison.Ordinal) == true
                            || service.ImplementationType == typeof(EmailSendingService)))
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

[CollectionDefinition(nameof(NotificationApiCollection))]
public sealed class NotificationApiCollection : ICollectionFixture<NotificationApiFixture>;
