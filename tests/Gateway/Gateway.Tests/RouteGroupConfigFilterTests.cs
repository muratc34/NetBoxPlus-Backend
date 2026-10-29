using Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Tests;

public sealed class RouteGroupConfigFilterTests
{
    private readonly RouteGroupConfigFilter _filter = new();

    [Theory]
    [InlineData("public", "/sample/{**remainder}")]
    [InlineData("admin", "/admin/sample/{**remainder}")]
    public async Task Route_GetsThePoliciesOfItsGroup(string group, string path)
    {
        var configured = await _filter.ConfigureRouteAsync(Route(path, group), cluster: null, TestContext.Current.CancellationToken);

        Assert.Equal(group, configured.CorsPolicy);
        Assert.Equal(group, configured.RateLimiterPolicy);
    }

    [Theory]
    [InlineData(null, "/sample/{**remainder}", "has no 'RouteGroup' metadata")]
    [InlineData("internal", "/sample/{**remainder}", "unknown group 'internal'")]
    [InlineData("admin", "/sample/{**remainder}", "must start with '/admin/'")]
    [InlineData("public", "/admin/sample/{**remainder}", "must not start with '/admin'")]
    [InlineData("public", "/Admin/{**remainder}", "must not start with '/admin'")]
    [InlineData("public", "/sample/{id}", "one catch-all")]
    [InlineData("public", "/sample/{**remainder}/edit", "one catch-all")]
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
        var route = Route("/admin/sample/{**remainder}", "admin") with { RateLimiterPolicy = "public" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _filter.ConfigureRouteAsync(route, cluster: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidRoute_StopsTheGatewayFromStarting()
    {
        await using var gateway = new GatewayFactory("http://127.0.0.1:1", new Dictionary<string, string?>
        {
            ["ReverseProxy:Routes:sample-admin:Metadata:RouteGroup"] = "public",
        });

        Assert.ThrowsAny<Exception>(() => gateway.CreateClient());
    }

    private static RouteConfig Route(string path, string? group) => new()
    {
        RouteId = "test",
        ClusterId = "sample",
        Match = new RouteMatch { Path = path },
        Metadata = group is null ? null : new Dictionary<string, string> { [RouteGroups.MetadataKey] = group },
    };
}
