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

    /// <summary>
    /// Calls from other systems, such as Stripe, that prove themselves with a signature the service
    /// checks. No browser session, no CORS, a limit of their own. Paths: <c>/{service}/webhooks/...</c>.
    /// </summary>
    public const string Webhooks = "webhooks";

    public const string WebhookSegment = "webhooks";

    public const string AdminPathPrefix = "/admin";

    /// <summary>Whether the path is <c>/{service}/webhooks/...</c>.</summary>
    public static bool IsWebhookPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 && segments[1].Equals(WebhookSegment, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAdminPath(string path)
        => path.Equals(AdminPathPrefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(AdminPathPrefix + "/", StringComparison.OrdinalIgnoreCase);
}
