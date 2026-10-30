using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Auth.Application.Abstractions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auth.IntegrationTests.Tokens;

/// <summary>
/// Discovery and JWKS as served by the auth API, and services validating its tokens through them.
/// No database needed.
/// </summary>
public sealed class TokenEndpointTests
{
    private static readonly AccessTokenSubject Jane = new(
        Guid.CreateVersion7(),
        "jane@example.com",
        ["Editor"],
        [ResourceServer.Permission]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discovery_NamesTheIssuerAndTheJwksAddress()
    {
        await using var auth = new AuthApiFactory("key-1", ("key-1", TestSigningKeys.NewPem()));

        var discovery = await auth.CreateClient().GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration", Cancellation);

        Assert.Equal("netboxplus-auth", discovery.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost/.well-known/jwks.json", discovery.GetProperty("jwks_uri").GetString());
    }

    [Fact]
    public async Task Jwks_PublishesEveryKeyWithoutPrivateParts()
    {
        await using var auth = new AuthApiFactory(
            "key-2",
            ("key-1", TestSigningKeys.NewPem()),
            ("key-2", TestSigningKeys.NewPem()));

        var response = await auth.CreateClient().GetAsync("/.well-known/jwks.json", Cancellation);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        var keys = JsonDocument.Parse(body).RootElement.GetProperty("keys").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
        Assert.Equal(["key-1", "key-2"], keys.Select(key => key.GetProperty("kid").GetString()));
        Assert.All(keys, key =>
        {
            Assert.Equal(
                ["alg", "crv", "kid", "kty", "use", "x", "y"],
                key.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal("EC", key.GetProperty("kty").GetString());
            Assert.Equal("P-256", key.GetProperty("crv").GetString());
            Assert.Equal("ES256", key.GetProperty("alg").GetString());
            Assert.Equal("sig", key.GetProperty("use").GetString());
        });

        // The published keys parse as JWKs that can verify.
        var keySet = new JsonWebKeySet(body);
        Assert.Equal(2, keySet.GetSigningKeys().Count);
    }

    [Fact]
    public async Task WithoutASigningKey_TheAuthServiceDoesNotStart()
    {
        await using var auth = new AuthApiFactory(activeKeyId: null);

        var exception = Assert.ThrowsAny<Exception>(() => auth.CreateClient());

        Assert.Contains("has no keys", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnInvalidSigningKey_TheAuthServiceDoesNotStart()
    {
        await using var auth = new AuthApiFactory("key-1", ("key-1", "-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----"));

        var exception = Assert.ThrowsAny<Exception>(() => auth.CreateClient());

        Assert.Contains("Signing key 'key-1' must be a P-256 private key", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthService_ValidatesItsOwnTokensWithoutAnAuthority()
    {
        await using var auth = new AuthApiFactory("key-1", ("key-1", TestSigningKeys.NewPem()));
        _ = auth.Server;

        var token = auth.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Jane).Value;
        var parameters = auth.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task Service_ValidatesAuthTokensThroughTheAuthority()
    {
        await using var auth = new AuthApiFactory("key-1", ("key-1", TestSigningKeys.NewPem()));
        await using var service = await ResourceServer.StartAsync(auth);
        var issuer = auth.Services.GetRequiredService<IAccessTokenIssuer>();

        var allowed = await service.GetProtectedAsync(issuer.Issue(Jane).Value);
        var forbidden = await service.GetProtectedAsync(issuer.Issue(Jane with { Permissions = ["auth:users:read"] }).Value);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(Jane.UserId.ToString(), await allowed.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Service_RejectsATokenOfAnotherType_SignedWithAnAuthKey()
    {
        var pem = TestSigningKeys.NewPem();
        await using var auth = new AuthApiFactory("key-1", ("key-1", pem));
        await using var service = await ResourceServer.StartAsync(auth);
        using var key = ECDsa.Create();
        key.ImportFromPem(pem);

        // Everything matches but the typ header, as for a JWT issued by Auth for some other purpose.
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            TokenType = "JWT",
            Issuer = "netboxplus-auth",
            Audience = "netboxplus",
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Jane.UserId.ToString(),
                ["permission"] = new[] { ResourceServer.Permission },
            },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = "key-1" }, SecurityAlgorithms.EcdsaSha256),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, (await service.GetProtectedAsync(token)).StatusCode);
    }

    [Fact]
    public async Task Rotation_NewKeyIsPickedUpAndOldTokensKeepValidating()
    {
        var oldPem = TestSigningKeys.NewPem();
        var newPem = TestSigningKeys.NewPem();

        // Before: only the old key. The service fetches the keys once, for the first token.
        await using var before = new AuthApiFactory("old", ("old", oldPem));
        await using var service = await ResourceServer.StartAsync(before);
        var oldToken = before.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Jane).Value;
        Assert.Equal(HttpStatusCode.OK, (await service.GetProtectedAsync(oldToken)).StatusCode);

        // After: both keys published, the new one active. The service has not seen the new key id yet.
        await using var after = new AuthApiFactory("new", ("old", oldPem), ("new", newPem));
        service.SwitchAuthTo(after);
        var newToken = after.Services.GetRequiredService<IAccessTokenIssuer>().Issue(Jane).Value;
        Assert.Equal("new", new JsonWebToken(newToken).Kid);

        // An unknown key id makes the service fetch the keys again; no restart, no configuration change.
        var status = HttpStatusCode.Unauthorized;
        for (var attempt = 0; attempt < 50 && status != HttpStatusCode.OK; attempt++)
        {
            status = (await service.GetProtectedAsync(newToken)).StatusCode;
            if (status != HttpStatusCode.OK)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
            }
        }

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(HttpStatusCode.OK, (await service.GetProtectedAsync(oldToken)).StatusCode);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
            }
        }

        return string.Join(Environment.NewLine, messages);
    }
}
