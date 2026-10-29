using Yarp.ReverseProxy.Configuration;

namespace Gateway.Routing;

/// <summary>
/// Applies a route's group to it on every configuration load: the CORS and rate limiter policies
/// come from the group, never from the route itself, so an admin route cannot end up with the
/// public limits by a configuration mistake. A route that breaks the rules fails the load: on
/// startup the gateway does not start, on a reload the previous configuration stays.
/// </summary>
internal sealed class RouteGroupConfigFilter : IProxyConfigFilter
{
    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel) => new(cluster);

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(route);

        if (route.Metadata is null || !route.Metadata.TryGetValue(RouteGroups.MetadataKey, out var group))
        {
            throw Invalid(route, $"has no '{RouteGroups.MetadataKey}' metadata; set it to '{RouteGroups.Public}' or '{RouteGroups.Admin}'.");
        }

        if (group is not (RouteGroups.Public or RouteGroups.Admin))
        {
            throw Invalid(route, $"has the unknown group '{group}'; use '{RouteGroups.Public}' or '{RouteGroups.Admin}'.");
        }

        if (route.CorsPolicy is not null || route.RateLimiterPolicy is not null)
        {
            throw Invalid(route, "sets its own CORS or rate limiter policy; both come from its group.");
        }

        if (string.IsNullOrWhiteSpace(route.Match.Path))
        {
            throw Invalid(route, "has no path; every route matches a path prefix of its group.");
        }

        var isAdminPath = RouteGroups.IsAdminPath(route.Match.Path);
        if (group == RouteGroups.Admin && !isAdminPath)
        {
            throw Invalid(route, $"is an admin route, so its path must start with '{RouteGroups.AdminPathPrefix}/'.");
        }

        if (group == RouteGroups.Public && isAdminPath)
        {
            throw Invalid(route, $"is a public route, so its path must not start with '{RouteGroups.AdminPathPrefix}'.");
        }

        if (!RoutePrefixes.TryParse(route, out _))
        {
            throw Invalid(route, "must match literal segments followed by one catch-all, such as '/sample/{**remainder}'.");
        }

        return new(route with { CorsPolicy = group, RateLimiterPolicy = group });
    }

    private static InvalidOperationException Invalid(RouteConfig route, string problem)
        => new($"Gateway route '{route.RouteId}' {problem}");
}
