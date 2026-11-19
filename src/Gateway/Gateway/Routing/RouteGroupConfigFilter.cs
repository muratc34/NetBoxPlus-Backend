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
            throw Invalid(route, $"has no '{RouteGroups.MetadataKey}' metadata; set it to '{RouteGroups.Public}', '{RouteGroups.Admin}' or '{RouteGroups.Webhooks}'.");
        }

        if (group is not (RouteGroups.Public or RouteGroups.Admin or RouteGroups.Webhooks))
        {
            throw Invalid(route, $"has the unknown group '{group}'; use '{RouteGroups.Public}', '{RouteGroups.Admin}' or '{RouteGroups.Webhooks}'.");
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

        if (group != RouteGroups.Admin && isAdminPath)
        {
            throw Invalid(route, $"is a {group} route, so its path must not start with '{RouteGroups.AdminPathPrefix}'.");
        }

        if (group == RouteGroups.Webhooks && !RouteGroups.IsWebhookPath(route.Match.Path))
        {
            throw Invalid(route, $"is a webhooks route, so its path must be '/{{service}}/{RouteGroups.WebhookSegment}/...'.");
        }

        if (!RoutePrefixes.TryParse(route, out _))
        {
            throw Invalid(route, "must match literal segments followed by one catch-all, such as '/catalog/{**remainder}'.");
        }

        // Webhooks come from servers, never from a browser: no CORS answers at all.
        var cors = group == RouteGroups.Webhooks ? DisableCors : group;
        return new(route with { CorsPolicy = cors, RateLimiterPolicy = group });
    }

    /// <summary>YARP's reserved policy name that turns CORS off for a route.</summary>
    public const string DisableCors = "Disable";

    private static InvalidOperationException Invalid(RouteConfig route, string problem)
        => new($"Gateway route '{route.RouteId}' {problem}");
}
