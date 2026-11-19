using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Gateway.Tests;

public sealed class RoutingTests : IAsyncLifetime
{
    private DownstreamService _downstream = null!;
    private GatewayFactory _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _downstream = await DownstreamService.StartAsync();
        _gateway = new GatewayFactory(_downstream.Address);
    }

    [Fact]
    public async Task PublicRoute_ForwardsWithoutTheServicePrefix()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/downstream/notes/42?view=full", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/notes/42?view=full", Assert.Single(_downstream.Received).PathAndQuery);
    }

    [Fact]
    public async Task AdminRoute_ForwardsToTheServiceAdminPath()
    {
        var response = await _gateway.CreateClient().DeleteAsync(new Uri("/admin/downstream/notes/42", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(_downstream.Received);
        Assert.Equal("DELETE", received.Method);
        Assert.Equal("/admin/notes/42", received.PathAndQuery);
    }

    [Theory]
    [InlineData("/downstream/notes", "/downstream/notes/1")]
    [InlineData("/admin/downstream/notes", "/admin/downstream/notes/1")]
    public async Task Location_IsRewrittenToTheGatewayPath(string path, string expected)
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _gateway.CreateClient().PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/downstream/admin/notes")]
    [InlineData("/downstream/ADMIN/notes")]
    [InlineData("/downstream/admin")]
    [InlineData("/downstream//admin/notes")]
    public async Task PublicRoute_DoesNotReachAdminPaths(string path)
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(_downstream.Received);
    }

    [Fact]
    public async Task PublicRoute_ForwardsPathsThatOnlyStartWithAdmin()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/downstream/administrators", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/administrators", Assert.Single(_downstream.Received).PathAndQuery);
    }

    [Fact]
    public async Task UnknownPath_IsNotFoundProblem()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/unknown/notes", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ClientTraceContext_IsNotContinued()
    {
        var clientTraceId = ActivityTraceId.CreateRandom().ToHexString();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/downstream/notes", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{clientTraceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        request.Headers.Add("baggage", "tenant=spoofed");

        var response = await _gateway.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var headers = Assert.Single(_downstream.Received).Headers;
        Assert.True(headers.TryGetValue("traceparent", out var forwarded), "The gateway's own trace context is forwarded.");
        Assert.DoesNotContain(clientTraceId, forwarded, StringComparison.Ordinal);
        Assert.False(headers.ContainsKey("baggage"));
    }

    [Fact]
    public async Task UnreachableService_IsBadGatewayProblem()
    {
        await using var gateway = new GatewayFactory("http://127.0.0.1:1");

        var response = await gateway.CreateClient().GetAsync(new Uri("/downstream/notes", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/downstream/notes", problem.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task Health_IsServedByTheGatewayItself()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_downstream.Received);
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _downstream.DisposeAsync();
    }
}
