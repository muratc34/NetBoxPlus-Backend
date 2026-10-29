using System.Net;
using System.Text.Json;

namespace Gateway.Tests;

public sealed class RateLimitingTests : IAsyncLifetime
{
    private DownstreamService _sample = null!;
    private GatewayFactory _gateway = null!;

    public async ValueTask InitializeAsync()
    {
        _sample = await DownstreamService.StartAsync();
        _gateway = new GatewayFactory(_sample.Address, new Dictionary<string, string?>
        {
            ["RateLimiting:Public:PermitLimit"] = "2",
            ["RateLimiting:Public:Window"] = "00:10:00",
            ["RateLimiting:Admin:PermitLimit"] = "1",
            ["RateLimiting:Admin:Window"] = "00:10:00",
        });
    }

    [Fact]
    public async Task RequestOverTheLimit_IsTooManyRequestsProblem()
    {
        var client = _gateway.CreateClient();
        var path = new Uri("/sample/notes", UriKind.Relative);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode);
        var rejected = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var retryAfter = rejected.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10));

        using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("RateLimit.Exceeded", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(429, problem.RootElement.GetProperty("status").GetInt32());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
        Assert.Equal(2, _sample.Received.Count);
    }

    [Fact]
    public async Task Groups_HaveSeparateLimits()
    {
        var client = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/admin/sample/notes", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync(new Uri("/admin/sample/notes", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/sample/notes", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _sample.DisposeAsync();
    }
}
