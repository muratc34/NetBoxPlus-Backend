using Yarp.ReverseProxy.Model;

namespace Gateway.Routing;

/// <summary>
/// A public route forwards <c>/sample/{**remainder}</c> as <c>/{remainder}</c>, so
/// <c>/sample/admin/notes</c> would reach the service's admin endpoint without passing through the
/// admin route's policies. This step answers such requests with 404 before they are forwarded.
/// Services still authorize their admin endpoints themselves; this only keeps the two groups apart.
/// </summary>
internal static partial class PublicRouteGuard
{
    public static Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var route = context.GetReverseProxyFeature().Route.Config;

        if (route.Metadata?.GetValueOrDefault(RouteGroups.MetadataKey) == RouteGroups.Public && TargetsAdmin(context))
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PublicRouteGuard));
            LogBlocked(logger, route.RouteId);

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next();
    }

    // The route values hold the part of the path that is forwarded (the catch-all), already decoded.
    private static bool TargetsAdmin(HttpContext context)
        => context.Request.RouteValues.Values
            .OfType<string>()
            .Any(value => RouteGroups.IsAdminPath("/" + value.TrimStart('/')));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Blocked a request for an admin path on public route {RouteId}")]
    private static partial void LogBlocked(ILogger logger, string routeId);
}
