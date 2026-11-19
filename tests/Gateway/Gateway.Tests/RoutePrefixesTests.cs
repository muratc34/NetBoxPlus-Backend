using Gateway.Routing;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Tests;

public sealed class RoutePrefixesTests
{
    private static readonly RoutePrefixes Public = Parse("/downstream/{**remainder}", RouteGroups.Public);
    private static readonly RoutePrefixes Admin = Parse("/admin/downstream/{**remainder}", RouteGroups.Admin);

    [Fact]
    public void Prefixes_FollowTheGroupConventions()
    {
        Assert.Equal(new RoutePrefixes("/downstream", string.Empty), Public);
        Assert.Equal(new RoutePrefixes("/admin/downstream", "/admin"), Admin);
    }

    [Theory]
    [InlineData("/notes/1", "/downstream/notes/1")]
    [InlineData("/notes/1?view=full", "/downstream/notes/1?view=full")]
    [InlineData("/", "/downstream/")]
    [InlineData("/admin/notes/1", null)]
    [InlineData("https://elsewhere.test/notes/1", null)]
    [InlineData("//elsewhere.test/notes/1", null)]
    [InlineData("notes/1", null)]
    public void PublicRoute_MapsServicePaths(string servicePath, string? expected)
    {
        Assert.Equal(expected, Public.ToGatewayPath(servicePath));
    }

    [Theory]
    [InlineData("/admin/notes/1", "/admin/downstream/notes/1")]
    [InlineData("/admin", "/admin/downstream")]
    [InlineData("/notes/1", null)]
    [InlineData("/administrators", null)]
    public void AdminRoute_MapsServicePaths(string servicePath, string? expected)
    {
        Assert.Equal(expected, Admin.ToGatewayPath(servicePath));
    }

    [Theory]
    [InlineData("/profiles", "/auth/profiles")]
    [InlineData("/profiles/1", "/auth/profiles/1")]
    [InlineData("/profiles?page=2", "/auth/profiles?page=2")]
    [InlineData("/sign-in", null)]
    [InlineData("/profilesx", null)]
    [InlineData("/admin/profiles", null)]
    public void ANarrowRoute_MapsOnlyItsScope(string servicePath, string? expected)
    {
        var narrow = Parse("/auth/profiles/{**remainder}", RouteGroups.Public, removePrefix: "/auth");

        Assert.Equal(new RoutePrefixes("/auth", string.Empty, "/profiles"), narrow);
        Assert.Equal(expected, narrow.ToGatewayPath(servicePath));
    }

    private static RoutePrefixes Parse(string path, string group, string? removePrefix = null)
    {
        var route = new RouteConfig
        {
            RouteId = "test",
            Match = new RouteMatch { Path = path },
            Metadata = new Dictionary<string, string> { [RouteGroups.MetadataKey] = group },
            Transforms = removePrefix is null ? null : [new Dictionary<string, string> { ["PathRemovePrefix"] = removePrefix }],
        };

        Assert.True(RoutePrefixes.TryParse(route, out var prefixes));
        return prefixes;
    }
}
