using Gateway.Routing;
using Microsoft.Extensions.Options;

namespace Gateway.Sessions;

/// <summary>
/// The session cookies. <c>__Host-</c> makes the browser insist on Secure, <c>Path=/</c> and no
/// <c>Domain</c>, so no other host can set or read them. Browsers treat <c>http://localhost</c> as
/// secure, so this also works locally.
/// </summary>
internal sealed class SessionCookies(IOptions<SessionOptions> options, TimeProvider timeProvider)
{
    public const string CustomerName = "__Host-nbp-session";
    public const string AdminName = "__Host-nbp-admin";

    public static string NameOf(SessionKind kind) => kind == SessionKind.Admin ? AdminName : CustomerName;

    /// <summary>The kind of session a route group works with: each application only ever sends its own cookie.</summary>
    public static SessionKind KindOf(string routeGroup) => routeGroup == RouteGroups.Admin ? SessionKind.Admin : SessionKind.Customer;

    public static string? Read(HttpContext context, SessionKind kind) => context.Request.Cookies[NameOf(kind)];

    /// <summary>
    /// The customer cookie outlives a browser restart ("stay signed in"); the admin cookie does not.
    /// Either way the session ends on the gateway when its refresh token or idle time runs out.
    /// </summary>
    public void Write(HttpContext context, SessionKind kind, string sessionId)
    {
        var cookie = Options();
        if (kind == SessionKind.Customer)
        {
            cookie.Expires = timeProvider.GetUtcNow() + options.Value.CustomerCookieLifetime;
        }

        context.Response.Cookies.Append(NameOf(kind), sessionId, cookie);
    }

    public static void Clear(HttpContext context, SessionKind kind) => context.Response.Cookies.Delete(NameOf(kind), Options());

    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
    };
}
