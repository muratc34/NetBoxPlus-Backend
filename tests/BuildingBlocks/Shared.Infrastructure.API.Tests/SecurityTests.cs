using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
