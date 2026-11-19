using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Routing;

/// <summary>
/// How a route maps paths, by the group conventions: a service path <c>{ServicePrefix}/{rest}</c> is
/// reached at <c>{GatewayPrefix}/{rest}</c>, and only paths under <paramref name="ServiceScope"/> belong
/// to the route.
/// <list type="bullet">
/// <item>public: <c>/catalog/movies/1</c> → <c>/movies/1</c> (service prefix empty)</item>
/// <item>admin: <c>/admin/catalog/movies/1</c> → <c>/admin/movies/1</c></item>
/// <item>a narrower route, such as <c>/auth/profiles/{**remainder}</c> removing <c>/auth</c>: gateway
/// prefix <c>/auth</c>, scope <c>/profiles</c>; the same holds for webhook routes.</item>
/// </list>
/// </summary>
internal sealed partial record RoutePrefixes(string GatewayPrefix, string ServicePrefix, string ServiceScope = "")
{
    public static bool TryParse(RouteConfig route, out RoutePrefixes prefixes)
    {
        prefixes = null!;

        if (route.Match.Path is not { } path
            || route.Metadata?.GetValueOrDefault(RouteGroups.MetadataKey) is not { } group
            || PathPattern().Match(path) is not { Success: true } match)
        {
            return false;
        }

        var prefix = match.Groups["prefix"].Value;
        if (group == RouteGroups.Admin)
        {
            prefixes = new RoutePrefixes(prefix, RouteGroups.AdminPathPrefix);
            return true;
        }

        // A route that removes less than it matches ("/billing/webhooks/..." removing "/billing")
        // keeps the rest of its match on the service side: that rest is its scope.
        var removed = route.Transforms?
            .Select(transform => transform.GetValueOrDefault("PathRemovePrefix"))
            .FirstOrDefault(value => value is not null);
        prefixes = removed is not null && prefix.StartsWith(removed + "/", StringComparison.OrdinalIgnoreCase)
            ? new RoutePrefixes(removed, string.Empty, prefix[removed.Length..])
            : new RoutePrefixes(prefix, string.Empty);
        return true;
    }

    /// <summary>
    /// A path the service returned (such as a <c>Location</c> header) as the client must call it
    /// through the gateway, or <see langword="null"/> when the path is not under this route.
    /// </summary>
    public string? ToGatewayPath(string servicePath)
    {
        if (!servicePath.StartsWith('/') || servicePath.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        if (ServicePrefix.Length == 0)
        {
            // A public route never exposes the service's admin paths, nor paths outside its scope.
            if (RouteGroups.IsAdminPath(servicePath) || !InScope(servicePath))
            {
                return null;
            }

            return GatewayPrefix + servicePath;
        }

        return servicePath.Equals(ServicePrefix, StringComparison.OrdinalIgnoreCase)
            || servicePath.StartsWith(ServicePrefix + "/", StringComparison.OrdinalIgnoreCase)
            ? GatewayPrefix + servicePath[ServicePrefix.Length..]
            : null;
    }

    private bool InScope(string servicePath)
        => ServiceScope.Length == 0
            || servicePath.Equals(ServiceScope, StringComparison.OrdinalIgnoreCase)
            || servicePath.StartsWith(ServiceScope + "/", StringComparison.OrdinalIgnoreCase)
            || servicePath.StartsWith(ServiceScope + "?", StringComparison.OrdinalIgnoreCase);

    // Literal segments followed by a single catch-all: "/admin/catalog/{**remainder}".
    [GeneratedRegex(@"^(?<prefix>(/[A-Za-z0-9._~-]+)+)/\{\*\*[A-Za-z][A-Za-z0-9]*\}$")]
    private static partial Regex PathPattern();
}
