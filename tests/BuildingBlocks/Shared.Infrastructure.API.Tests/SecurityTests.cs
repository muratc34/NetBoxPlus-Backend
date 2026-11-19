using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Application;
using Shared.Infrastructure.API.Security;

namespace Shared.Infrastructure.API.Tests;

public sealed class SecurityTests
{
    private const string WritePermission = "catalog:movies:write";

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401ProblemDetails()
    {
        await using var app = await Start();

        var response = await app.Client.GetAsync("/movies/write", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutThePermission_Returns403ProblemDetails()
    {
        await using var app = await Start();

        var response = await Send(app, "/movies/write", app.CreateToken(Guid.CreateVersion7(), ["catalog:movies:read"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithThePermission_IsAllowed()
    {
        await using var app = await Start();

        var response = await Send(app, "/movies/write", app.CreateToken(Guid.CreateVersion7(), [WritePermission]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        await using var app = await Start();

        var token = app.CreateToken(Guid.CreateVersion7(), [WritePermission], expires: DateTime.UtcNow.AddMinutes(-2));
        var response = await Send(app, "/movies/write", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenOfAnotherType_IsRejected()
    {
        await using var app = await Start();

        var token = app.CreateToken(Guid.CreateVersion7(), [WritePermission], tokenType: "JWT");
        var response = await Send(app, "/movies/write", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenSignedWithAnotherAlgorithm_IsRejected_EvenWithATrustedKey()
    {
        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "rsa" };

        // The RSA key is trusted, so only the algorithm restriction can turn the token away.
        await using var app = await TestApp.StartAsync(Map, withAuthentication: true, configure: builder =>
            builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.TokenValidationParameters.IssuerSigningKeys = [rsaKey]));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            TokenType = AccessTokenFormat.Type,
            Issuer = TestApp.Issuer,
            Audience = TestApp.Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = Guid.CreateVersion7().ToString(),
                [NetBoxPlusClaimTypes.Permission] = new[] { WritePermission },
            },
            SigningCredentials = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256),
        });
        var response = await Send(app, "/movies/write", token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WithoutAnAuthority_TheApplicationDoesNotStart()
    {
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() => TestApp.StartAsync(
            Map,
            configure: builder => builder.Services.AddJwtAuthentication()));

        Assert.Contains("Authority", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithAnAuthority_TheApplicationStarts()
    {
        await using var app = await TestApp.StartAsync(Map, configure: builder =>
        {
            builder.Configuration["Authentication:Schemes:Bearer:Authority"] = "https://auth.test";
            builder.Services.AddJwtAuthentication();
        });

        Assert.NotNull(app.Client);
    }

    [Fact]
    public async Task CurrentUser_ReadsTheSubjectAndPermissionsAsIssued()
    {
        await using var app = await Start();
        var userId = Guid.CreateVersion7();

        var response = await Send(app, "/me", app.CreateToken(userId, [WritePermission]));
        var me = await response.Content.ReadFromJsonAsync<Me>(TestContext.Current.CancellationToken);

        Assert.Equal(new Me(true, userId, true, false), me);
    }

    [Fact]
    public async Task CurrentUser_ForAnonymousCaller_HasNoIdentity()
    {
        await using var app = await Start();

        var me = await app.Client.GetFromJsonAsync<Me>("/me", TestContext.Current.CancellationToken);

        Assert.Equal(new Me(false, null, false, false), me);
    }

    [Fact]
    public void HasPermissionAttribute_UsesThePermissionPolicy()
    {
        var attribute = new HasPermissionAttribute(WritePermission);

        Assert.Equal("permission:" + WritePermission, attribute.Policy);
    }

    private static Task<TestApp> Start() => TestApp.StartAsync(Map, withAuthentication: true);

    private static void Map(WebApplication app)
    {
        app.MapGet("/movies/write", () => TypedResults.Ok()).RequirePermission(WritePermission);
        app.MapGet("/me", (ICurrentUser user) => new Me(
            user.IsAuthenticated,
            user.Id,
            user.HasPermission(WritePermission),
            user.HasPermission("billing:plans:write")));
    }

    private static Task<HttpResponseMessage> Send(TestApp app, string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return app.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record Me(bool IsAuthenticated, Guid? Id, bool CanWriteMovies, bool CanWritePlans);
}
