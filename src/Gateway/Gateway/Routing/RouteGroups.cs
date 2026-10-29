namespace Gateway.Routing;

/// <summary>
/// Every route belongs to exactly one group, declared in its <c>Metadata</c>. The group decides the
/// route's CORS and rate limiter policies, which carry the same names.
/// </summary>
internal static class RouteGroups
{
    public const string MetadataKey = "RouteGroup";

    /// <summary>The customer-facing application. Paths: <c>/{service}/...</c>.</summary>
    public const string Public = "public";

    /// <summary>The admin panel. Paths: <c>/admin/{service}/...</c>.</summary>
    public const string Admin = "admin";

    public const string AdminPathPrefix = "/admin";

    public static bool IsAdminPath(string path)
        => path.Equals(AdminPathPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(AdminPathPrefix + "/", StringComparison.OrdinalIgnoreCase);
}
