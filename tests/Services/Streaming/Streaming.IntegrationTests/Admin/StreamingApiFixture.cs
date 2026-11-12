using System.Net.Http.Headers;
using System.Security.Cryptography;
using Streaming.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NetBoxPlus.Testing;
using Streaming.Infrastructure;
using Streaming.Infrastructure.Media;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

namespace Streaming.IntegrationTests.Admin;

/// <summary>
/// The streaming API in memory against a real PostgreSQL container, migrated by the service's own
/// migrations; tests sign their tokens with a key of their own. Without Docker the tests are skipped.
/// </summary>
public sealed class StreamingApiFixture : IAsyncLifetime
{
    private const string Issuer = "https://auth.test";

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private PostgreSqlContainer? _postgres;
    private AzuriteContainer? _azurite;
    private StreamingApiFactory? _factory;

    public bool IsAvailable { get; private set; }

    public string VideosConnectionString { get; private set; } = string.Empty;


    public string SkipReason { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder("postgres:18.6-alpine").Build();
            _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.37.0").WithCommand("--skipApiVersionCheck").Build();
            await Task.WhenAll(_postgres.StartAsync(), _azurite.StartAsync());
        }
        catch (Exception exception) when (DockerAvailability.CanSkip(exception))
        {
            SkipReason = $"Docker is not available: {exception.Message}";
            return;
        }

        VideosConnectionString = _azurite.GetConnectionString();
        await VideoStorageSetup.EnsureAsync(VideosConnectionString, [], []);
        _factory = new StreamingApiFactory(_postgres.GetConnectionString(), VideosConnectionString, _signingKey);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StreamingDbContext>().Database.MigrateAsync();

        IsAvailable = true;
    }

    /// <summary>A client whose token holds exactly the given permissions.</summary>
    public HttpClient CreateClient(params string[] permissions) => CreateClientFor(Guid.CreateVersion7(), permissions);

    /// <summary>A client signed in as the given user.</summary>
    public HttpClient CreateClientFor(Guid userId, params string[] permissions)
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, permissions));
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

        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
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

    private sealed class StreamingApiFactory(string database, string videos, ECDsa signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("Messaging:ConnectionString", "amqp://localhost:1/");
            builder.UseSetting("ConnectionStrings:Videos", videos);
            builder.UseSetting("Transcoder:FfmpegPath", FfmpegAvailability.FfmpegPath ?? "ffmpeg");
            builder.UseSetting("Transcoder:FfprobePath", FfmpegAvailability.FfprobePath ?? "ffprobe");

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

                // The cleanup runs when a test asks; the transcoder is a process of its own, driven by tests here.
                services.Remove(services.Single(service => service.ImplementationType == typeof(ExpiredUploadCleanupService)));
                services.AddTranscoding(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Transcoder:FfmpegPath"] = FfmpegAvailability.FfmpegPath ?? "ffmpeg",
                        ["Transcoder:FfprobePath"] = FfmpegAvailability.FfprobePath ?? "ffprobe",
                    })
                    .Build());

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

[CollectionDefinition(nameof(StreamingApiCollection))]
public sealed class StreamingApiCollection : ICollectionFixture<StreamingApiFixture>;
