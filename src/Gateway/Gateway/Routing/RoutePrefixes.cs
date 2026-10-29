using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Configuration;

namespace Gateway.Routing;

/// <summary>
/// How a route maps paths, by the group conventions: the route matches
/// <c>{GatewayPrefix}/{**catch-all}</c> and the service sees <c>{ServicePrefix}/{catch-all}</c>.
/// <list type="bullet">
/// <item>public: <c>/sample/notes/1</c> → <c>/notes/1</c> (service prefix empty)</item>
/// <item>admin: <c>/admin/sample/notes/1</c> → <c>/admin/notes/1</c></item>
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

        prefixes = new RoutePrefixes(match.Groups["prefix"].Value, group == RouteGroups.Admin ? RouteGroups.AdminPathPrefix : string.Empty);
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

    // Literal segments followed by a single catch-all: "/admin/sample/{**remainder}".
    [GeneratedRegex(@"^(?<prefix>(/[A-Za-z0-9._~-]+)+)/\{\*\*[A-Za-z][A-Za-z0-9]*\}$")]
    private static partial Regex PathPattern();
}
