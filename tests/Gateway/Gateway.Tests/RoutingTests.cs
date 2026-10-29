using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace Gateway.Tests;

public sealed class RoutingTests : IAsyncLifetime
{
    private DownstreamService _sample = null!;
    private GatewayFactory _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _sample = await DownstreamService.StartAsync();
        _gateway = new GatewayFactory(_sample.Address);
    }

    [Fact]
    public async Task PublicRoute_ForwardsWithoutTheServicePrefix()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/sample/notes/42?view=full", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/notes/42?view=full", Assert.Single(_sample.Received).PathAndQuery);
    }

    [Fact]
    public async Task AdminRoute_ForwardsToTheServiceAdminPath()
    {
        var response = await _gateway.CreateClient().DeleteAsync(new Uri("/admin/sample/notes/42", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var received = Assert.Single(_sample.Received);
        Assert.Equal("DELETE", received.Method);
        Assert.Equal("/admin/notes/42", received.PathAndQuery);
    }

    [Theory]
    [InlineData("/sample/notes", "/sample/notes/1")]
    [InlineData("/admin/sample/notes", "/admin/sample/notes/1")]
    public async Task Location_IsRewrittenToTheGatewayPath(string path, string expected)
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _gateway.CreateClient().PostAsync(new Uri(path, UriKind.Relative), content, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/sample/admin/notes")]
    [InlineData("/sample/ADMIN/notes")]
    [InlineData("/sample/admin")]
    [InlineData("/sample//admin/notes")]
    public async Task PublicRoute_DoesNotReachAdminPaths(string path)
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(_sample.Received);
    }

    [Fact]
    public async Task PublicRoute_ForwardsPathsThatOnlyStartWithAdmin()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/sample/administrators", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/administrators", Assert.Single(_sample.Received).PathAndQuery);
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
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/sample/notes", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{clientTraceId}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        request.Headers.Add("baggage", "tenant=spoofed");

        var response = await _gateway.CreateClient().SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var headers = Assert.Single(_sample.Received).Headers;
        Assert.True(headers.TryGetValue("traceparent", out var forwarded), "The gateway's own trace context is forwarded.");
        Assert.DoesNotContain(clientTraceId, forwarded, StringComparison.Ordinal);
        Assert.False(headers.ContainsKey("baggage"));
    }

    [Fact]
    public async Task UnreachableService_IsBadGatewayProblem()
    {
        await using var gateway = new GatewayFactory("http://127.0.0.1:1");

        var response = await gateway.CreateClient().GetAsync(new Uri("/sample/notes", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/sample/notes", problem.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task Health_IsServedByTheGatewayItself()
    {
        var response = await _gateway.CreateClient().GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_sample.Received);
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _sample.DisposeAsync();
    }
}
