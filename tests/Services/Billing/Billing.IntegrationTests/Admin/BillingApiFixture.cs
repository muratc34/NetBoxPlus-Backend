using System.Net.Http.Headers;
using System.Security.Cryptography;
using Billing.Application.Abstractions;
using Billing.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

namespace Billing.IntegrationTests.Admin;

/// <summary>
/// The billing API in memory against a real PostgreSQL container, migrated by the service's own
/// migrations; tests sign their tokens with a key of their own. Without Docker the tests are skipped.
/// </summary>
public sealed class BillingApiFixture : IAsyncLifetime
{
    private const string Issuer = "https://auth.test";

    private readonly ECDsa _signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private PostgreSqlContainer? _postgres;
    private BillingApiFactory? _factory;
    private string _database = string.Empty;

    public bool IsAvailable { get; private set; }

    /// <summary>The payment provider the API talks to; tests look at what it was asked and decide how it answers.</summary>
    public FakePaymentGateway Payments { get; } = new();

    /// <summary>What the API checks webhook signatures with; tests sign with it.</summary>
    public const string WebhookSecret = "whsec_test_only_not_a_real_secret";

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

        _database = _postgres.GetConnectionString();
        _factory = new BillingApiFactory(_database, _signingKey, Payments);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync();

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

    /// <summary>The same API without a Stripe key, as on a machine without a Stripe account.</summary>
    public HttpClient CreateClientWithoutPayments(Guid userId)
    {
        Assert.SkipUnless(IsAvailable, SkipReason);

        var client = new BillingApiFactory(_database, _signingKey, payments: null).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(userId, []));
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

    private sealed class BillingApiFactory(string database, ECDsa signingKey, FakePaymentGateway? payments) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": that would load the developer's user secrets on top of these settings.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database);
            builder.UseSetting("Messaging:ConnectionString", "amqp://localhost:1/");
            builder.UseSetting("Billing:Checkout:SuccessUrl", "https://app.test/subscribed");
            builder.UseSetting("Billing:Checkout:CancelUrl", "https://app.test/plans");
            builder.UseSetting("Billing:Checkout:PortalReturnUrl", "https://app.test/account");

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

                if (payments is not null)
                {
                    services.PostConfigure<Billing.Infrastructure.Payments.StripeOptions>(options => options.WebhookSecret = WebhookSecret);
                    services.RemoveAll<IPaymentGateway>();
                    services.AddSingleton<IPaymentGateway>(payments);
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

[CollectionDefinition(nameof(BillingApiCollection))]
public sealed class BillingApiCollection : ICollectionFixture<BillingApiFixture>;
