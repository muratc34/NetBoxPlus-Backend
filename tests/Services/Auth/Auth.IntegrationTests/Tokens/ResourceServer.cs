using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.API.Security;

namespace Auth.IntegrationTests.Tokens;

/// <summary>
/// Any service: the shared <c>AddJwtAuthentication</c> with an <c>Authority</c>, as configured in
/// production, except that discovery and JWKS requests reach an in-memory auth API.
/// </summary>
internal sealed class ResourceServer : IAsyncDisposable
{
    public const string Permission = "sample:notes:delete";

    private readonly WebApplication _app;
    private readonly SwitchableHandler _backchannel;

    private ResourceServer(WebApplication app, SwitchableHandler backchannel)
    {
        _app = app;
        _backchannel = backchannel;
        Client = app.GetTestClient();
    }

    public HttpClient Client { get; }

    public static async Task<ResourceServer> StartAsync(AuthApiFactory auth)
    {
        var backchannel = new SwitchableHandler(auth.Server.CreateHandler());

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Authentication:Schemes:Bearer:Authority"] = "http://auth.test";
        builder.Configuration["Authentication:Schemes:Bearer:RequireHttpsMetadata"] = "false";
        builder.Configuration["Authentication:Schemes:Bearer:ValidAudiences:0"] = "netboxplus";
        builder.Services.AddJwtAuthentication();

        // Configure, not PostConfigure: JwtBearer builds its backchannel client in its own PostConfigure.
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.BackchannelHttpHandler = backchannel);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protected", (HttpContext context) => context.User.Identity!.Name).RequirePermission(Permission);

        await app.StartAsync();
        return new ResourceServer(app, backchannel);
    }

    /// <summary>Sends later discovery and JWKS requests to another auth API, as after a redeploy.</summary>
    public void SwitchAuthTo(AuthApiFactory auth) => _backchannel.Target = new HttpMessageInvoker(auth.Server.CreateHandler());

    public Task<HttpResponseMessage> GetProtectedAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }

    private sealed class SwitchableHandler(HttpMessageHandler initial) : HttpMessageHandler
    {
        public HttpMessageInvoker Target { get; set; } = new(initial);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Target.SendAsync(request, cancellationToken);
    }
}
