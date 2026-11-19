using Gateway.Cors;
using Gateway.Routing;
using Microsoft.Extensions.Options;

namespace Gateway.Sessions;

/// <summary>Cross-site request checks for requests that rely on the session cookie.</summary>
internal static class RequestGuards
{
    public const string CsrfHeader = "X-CSRF-Token";

    /// <summary>
    /// A cookie travels with any request to the gateway, including ones another site makes the
    /// browser send. Changing requests therefore also have to echo the session's CSRF token, which
    /// only the application's own pages can read (from <c>GET /session</c>).
    /// </summary>
    public static bool IsCsrfSafe(HttpContext context, BrowserSession session)
        => HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method)
            || SessionSecrets.AreEqual(context.Request.Headers[CsrfHeader].ToString(), session.CsrfToken);

    /// <summary>
    /// Whether a browser request comes from one of the group's origins. Requests without an
    /// <c>Origin</c> header (same-origin GETs, non-browser clients) pass; a browser always sends one
    /// on a cross-origin POST. Guards sign-in and registration, which have no session to hold a
    /// CSRF token yet (login CSRF).
    /// </summary>
    public static bool IsAllowedOrigin(HttpContext context, string routeGroup)
    {
        if (context.Request.Headers.Origin.ToString() is not { Length: > 0 } origin)
        {
            return true;
        }

        var cors = context.RequestServices.GetRequiredService<IOptions<GatewayCorsOptions>>().Value;
        var allowed = routeGroup == RouteGroups.Admin ? cors.AdminOrigins : cors.PublicOrigins;
        return allowed.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Endpoint filter: answers 403 to a request from an origin outside the group.</summary>
    public static EndpointFilterDelegate RequireAllowedOrigin(string routeGroup, EndpointFilterDelegate next)
        => async context => IsAllowedOrigin(context.HttpContext, routeGroup)
            ? await next(context).ConfigureAwait(false)
            : GatewayProblem.OriginNotAllowed;
}
