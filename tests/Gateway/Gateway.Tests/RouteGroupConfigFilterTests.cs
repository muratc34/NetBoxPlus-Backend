using Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Tests;

public sealed class RouteGroupConfigFilterTests
{
    private readonly RouteGroupConfigFilter _filter = new();

    [Theory]
    [InlineData("public", "/downstream/{**remainder}")]
    [InlineData("admin", "/admin/downstream/{**remainder}")]
    public async Task Route_GetsThePoliciesOfItsGroup(string group, string path)
    {
        var configured = await _filter.ConfigureRouteAsync(Route(path, group), cluster: null, TestContext.Current.CancellationToken);

        Assert.Equal(group, configured.CorsPolicy);
        Assert.Equal(group, configured.RateLimiterPolicy);
    }

    [Theory]
    [InlineData(null, "/downstream/{**remainder}", "has no 'RouteGroup' metadata")]
    [InlineData("internal", "/downstream/{**remainder}", "unknown group 'internal'")]
    [InlineData("admin", "/downstream/{**remainder}", "must start with '/admin/'")]
    [InlineData("public", "/admin/downstream/{**remainder}", "must not start with '/admin'")]
    [InlineData("public", "/Admin/{**remainder}", "must not start with '/admin'")]
    [InlineData("public", "/downstream/{id}", "one catch-all")]
    [InlineData("public", "/downstream/{**remainder}/edit", "one catch-all")]
    [InlineData("admin", "/admin/{service}/{**remainder}", "one catch-all")]
    public async Task InvalidRoute_IsRejected(string? group, string path, string expected)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _filter.ConfigureRouteAsync(Route(path, group), cluster: null, TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RouteWithItsOwnPolicy_IsRejected()
    {
        var route = Route("/admin/downstream/{**remainder}", "admin") with { RateLimiterPolicy = "public" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _filter.ConfigureRouteAsync(route, cluster: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidRoute_StopsTheGatewayFromStarting()
    {
        await using var gateway = new GatewayFactory("http://127.0.0.1:1", new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:downstream-admin:Metadata:RouteGroup"] = "public",
        });

        Assert.ThrowsAny<Exception>(() => gateway.CreateClient());
    }

    private static RouteConfig Route(string path, string? group) => new()
    {
        RouteId = "test",
        ClusterId = "downstream",
        Match = new RouteMatch { Path = path },
        Metadata = group is null ? null : new Dictionary<string, string> { [RouteGroups.MetadataKey] = group },
    };
}
