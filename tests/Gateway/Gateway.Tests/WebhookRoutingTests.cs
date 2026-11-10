using System.Net;

namespace Gateway.Tests;

public sealed class WebhookRoutingTests : IAsyncLifetime
{
    private DownstreamService _downstream = null!;
    private GatewayFactory _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _downstream = await DownstreamService.StartAsync();
        _gateway = new GatewayFactory(_downstream.Address);
    }

    [Fact]
    public async Task Webhook_ReachesTheService_WithoutCookiesOrCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/downstream/webhooks/stripe") { Content = new StringContent("{}") };
        request.Headers.Add("Cookie", "__Host-nbp-session=someone; __Host-nbp-admin=someone-else");
        request.Headers.Add("Authorization", "Bearer forwarded-by-nobody");
        request.Headers.Add("Stripe-Signature", "t=1,v1=abc");

        // A session cookie on a public route would be looked up (and fail here: nothing listens on Valkey).
        var response = await _gateway.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var received = Assert.Single(_downstream.Received);
        Assert.Equal("/webhooks/stripe", received.PathAndQuery);
        Assert.False(received.Headers.ContainsKey("Cookie"));
        Assert.False(received.Headers.ContainsKey("Authorization"));
        Assert.Equal("t=1,v1=abc", received.Headers["Stripe-Signature"]);
        Assert.Equal("/downstream/webhooks/stripe/1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Webhook_CannotReachAdminPaths()
    {
        var response = await _gateway.CreateClient().PostAsync(new Uri("/downstream/webhooks/admin/plans", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_downstream.Received);
    }

    [Fact]
    public async Task Webhook_AnswersNoBrowser()
    {
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/downstream/webhooks/stripe");
        preflight.Headers.Add("Origin", GatewayFactory.PublicOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await _gateway.CreateClient().SendAsync(preflight, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _downstream.DisposeAsync();
    }
}
