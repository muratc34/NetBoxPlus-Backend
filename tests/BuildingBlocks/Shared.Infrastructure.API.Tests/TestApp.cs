using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Infrastructure.API.Hosting;
using Shared.Infrastructure.API.Security;

namespace Shared.Infrastructure.API.Tests;

/// <summary>
/// An in-memory ASP.NET Core application wired the way a service is: service defaults first, then
/// (optionally) JWT authentication, then the test's endpoints.
/// </summary>
internal sealed class TestApp : IAsyncDisposable
{
    public const string Issuer = "https://auth.test";
    public const string Audience = "netboxplus-tests";

    private readonly WebApplication _app;

    private TestApp(WebApplication app, RSA signingKey)
    {
        _app = app;
        SigningKey = signingKey;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => _app.Services;

    private RSA SigningKey { get; }

    public static async Task<TestApp> StartAsync(
        Action<WebApplication> mapEndpoints,
        bool withAuthentication = false,
        Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.AddServiceDefaults();
        builder.Logging.AddFakeLogging();

        // Stands in for the Auth service: tokens are signed with this key instead of one found via JWKS.
        var signingKey = RSA.Create(2048);
        if (withAuthentication)
        {
            builder.Services.AddJwtAuthentication();
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters.ValidIssuer = Issuer;
                options.TokenValidationParameters.ValidAudience = Audience;
                options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(signingKey);
            });
        }

        configure?.Invoke(builder);

        var app = builder.Build();
        app.UseServiceDefaults();

        if (withAuthentication)
        {
            app.UseAuthentication();
            app.UseAuthorization();
        }

        mapEndpoints(app);
        app.MapDefaultEndpoints();

        await app.StartAsync();
        return new TestApp(app, signingKey);
    }

    public string CreateToken(Guid userId, IEnumerable<string>? permissions = null, DateTime? expires = null)
    {
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = userId.ToString(),
            [NetBoxPlusClaimTypes.Permission] = (permissions ?? []).ToArray(),
        };

        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(15);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = claims,
            NotBefore = expiresAt.AddMinutes(-30),
            IssuedAt = expiresAt.AddMinutes(-30),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(SigningKey), SecurityAlgorithms.RsaSha256),
        });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
        SigningKey.Dispose();
    }
}
