using Gateway.Routing;
using Microsoft.Net.Http.Headers;
using Yarp.ReverseProxy.Model;

namespace Gateway.Sessions;

/// <summary>
/// The step of the proxy pipeline that turns the browser's session cookie into the bearer token the
/// services expect. A route only honours the cookie of its own group: the admin panel's session
/// never works on customer routes and the other way round. Without a cookie the request passes
/// unchanged apart from its cookies, so clients with their own bearer token keep working. Cookies
/// never reach the services.
/// </summary>
internal static class SessionProxy
{
    public static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var routeGroup = context.GetReverseProxyFeature().Route.Config.Metadata?.GetValueOrDefault(RouteGroups.MetadataKey) ?? RouteGroups.Public;

        // A webhook is not a browser: whatever cookie or token it carries is not ours to pass on.
        if (routeGroup == RouteGroups.Webhooks)
        {
            context.Request.Headers.Remove(HeaderNames.Cookie);
            context.Request.Headers.Remove(HeaderNames.Authorization);
            await next().ConfigureAwait(false);
            return;
        }
        var kind = SessionCookies.KindOf(routeGroup);

        if (SessionCookies.Read(context, kind) is null)
        {
            // The other group's session cookie may still be there; it never goes further.
            context.Request.Headers.Remove(HeaderNames.Cookie);
            await next().ConfigureAwait(false);
            return;
        }

        var sessions = context.RequestServices.GetRequiredService<SessionManager>();

        // A cookie whose session is gone is not treated as anonymous: the application believes it
        // is signed in and must learn otherwise.
        if (await sessions.LoadAsync(context, kind).ConfigureAwait(false) is not { } loaded)
        {
            SessionCookies.Clear(context, kind);
            await GatewayProblem.SessionExpired.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        if (!RequestGuards.IsCsrfSafe(context, loaded.Session))
        {
            await GatewayProblem.CsrfInvalid.ExecuteAsync(context).ConfigureAwait(false);
            return;
        }

        var outcome = await sessions.EnsureFreshAsync(loaded).ConfigureAwait(false);
        if (outcome.Status != RefreshStatus.Fresh)
        {
            if (outcome.Status == RefreshStatus.Ended)
            {
                SessionCookies.Clear(context, kind);
                await GatewayProblem.SessionExpired.ExecuteAsync(context).ConfigureAwait(false);
            }
            else
            {
                await GatewayProblem.AuthUnavailable.ExecuteAsync(context).ConfigureAwait(false);
            }

            return;
        }

        await sessions.TouchAsync(loaded).ConfigureAwait(false);

        // The services get the token and nothing of the browser session: no cookies (the session
        // id would let anyone who logs requests take over the session) and no CSRF token.
        var headers = context.Request.Headers;
        headers.Authorization = "Bearer " + outcome.Session!.AccessToken;
        headers.Remove(HeaderNames.Cookie);
        headers.Remove(RequestGuards.CsrfHeader);

        await next().ConfigureAwait(false);
    }
}
