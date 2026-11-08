using System.Net;

namespace Gateway.Tests;

public sealed class CorsTests : IAsyncLifetime
{
    private DownstreamService _downstream = null!;
    private GatewayFactory _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _downstream = await DownstreamService.StartAsync();
        _gateway = new GatewayFactory(_downstream.Address);
    }

    [Theory]
    [InlineData("/downstream/notes", GatewayFactory.PublicOrigin)]
    [InlineData("/admin/downstream/notes", GatewayFactory.AdminOrigin)]
    public async Task Preflight_FromTheGroupsOrigin_IsAllowed(string path, string origin)
    {
        var response = await PreflightAsync(path, origin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Empty(_downstream.Received);
    }

    [Theory]
    [InlineData("/downstream/notes", GatewayFactory.AdminOrigin)]
    [InlineData("/admin/downstream/notes", GatewayFactory.PublicOrigin)]
    [InlineData("/downstream/notes", "https://elsewhere.test")]
    public async Task Preflight_FromAnotherOrigin_IsNotAllowed(string path, string origin)
    {
        var response = await PreflightAsync(path, origin);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private async Task<HttpResponseMessage> PreflightAsync(string path, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri(path, UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        return await _gateway.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _downstream.DisposeAsync();
    }
}
