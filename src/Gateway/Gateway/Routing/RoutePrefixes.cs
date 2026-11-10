using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Routing;

/// <summary>
/// How a route maps paths, by the group conventions: the route matches
/// <c>{GatewayPrefix}/{**catch-all}</c> and the service sees <c>{ServicePrefix}/{catch-all}</c>.
/// <list type="bullet">
/// <item>public: <c>/catalog/movies/1</c> → <c>/movies/1</c> (service prefix empty)</item>
/// <item>admin: <c>/admin/catalog/movies/1</c> → <c>/admin/movies/1</c></item>
/// </list>
/// </summary>
internal sealed partial record RoutePrefixes(string GatewayPrefix, string ServicePrefix)
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
        prefixes = group switch
        {
            RouteGroups.Admin => new RoutePrefixes(prefix, RouteGroups.AdminPathPrefix),

            // "/billing/webhooks/{**remainder}" forwards as "/webhooks/{remainder}", like the public route.
            RouteGroups.Webhooks => new RoutePrefixes(prefix[..prefix.IndexOf('/', 1)], string.Empty),
            _ => new RoutePrefixes(prefix, string.Empty),
        };
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
            // A public route never exposes the service's admin paths.
            return RouteGroups.IsAdminPath(servicePath) ? null : GatewayPrefix + servicePath;
        }

        return servicePath.Equals(ServicePrefix, StringComparison.OrdinalIgnoreCase)
            || servicePath.StartsWith(ServicePrefix + "/", StringComparison.OrdinalIgnoreCase)
            ? GatewayPrefix + servicePath[ServicePrefix.Length..]
            : null;
    }

    // Literal segments followed by a single catch-all: "/admin/catalog/{**remainder}".
    [GeneratedRegex(@"^(?<prefix>(/[A-Za-z0-9._~-]+)+)/\{\*\*[A-Za-z][A-Za-z0-9]*\}$")]
    private static partial Regex PathPattern();
}
