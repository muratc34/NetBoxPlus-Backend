using Gateway.RateLimiting;
using Gateway.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gateway.Sessions;

public sealed record CredentialsRequest(string? Email, string? Password);

public sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);

/// <summary>
/// The signed-in user as the session's access token describes them, and the CSRF token for
/// changing requests. Tokens themselves never leave the gateway.
/// </summary>
public sealed record SessionResponse(Guid UserId, string Email, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, string CsrfToken);

/// <summary>
/// The gateway's own endpoints for browser sessions: <c>/session/...</c> for the customer
/// application and <c>/admin/session/...</c> for the admin panel, each with its own cookie. They
/// call the auth service and keep its tokens; the browser only gets the cookie.
/// </summary>
internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var customer = endpoints.MapGroup("/session").RequireCors(RouteGroups.Public);
        MapCommon(customer, SessionKind.Customer, RouteGroups.Public);

        customer.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next));

        customer.MapPost("/password", ChangePasswordAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next));

        var admin = endpoints.MapGroup("/admin/session").RequireCors(RouteGroups.Admin);
        MapCommon(admin, SessionKind.Admin, RouteGroups.Admin);

        return endpoints;
    }

    private static void MapCommon(RouteGroupBuilder group, SessionKind kind, string routeGroup)
    {
        group.MapPost("/sign-in", (
                [FromBody] CredentialsRequest request,
                HttpContext context,
                [FromServices] AuthClient auth,
                [FromServices] SessionManager sessions,
                CancellationToken cancellationToken) => SignInAsync(request, kind, context, auth, sessions, cancellationToken))
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next));

        group.MapGet("/", (HttpContext context, [FromServices] SessionManager sessions) => GetAsync(kind, context, sessions))
            .RequireRateLimiting(routeGroup);

        group.MapPost("/sign-out", (HttpContext context, [FromServices] SessionManager sessions) => SignOutAsync(kind, context, sessions))
            .RequireRateLimiting(routeGroup)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next));
    }

    private static async Task<IResult> SignInAsync(
        CredentialsRequest request,
        SessionKind kind,
        HttpContext context,
        AuthClient auth,
        SessionManager sessions,
        CancellationToken cancellationToken)
    {
        var result = await auth.SignInAsync(request.Email, request.Password, kind, cancellationToken);
        if (result.Pair is null)
        {
            return new AuthProblemResult(result);
        }

        var started = await sessions.StartAsync(context, kind, result.Pair);
        return NoStore(context, TypedResults.Ok(Describe(started.Session)));
    }

    /// <summary>Creates the account and signs it in: one call for the customer application.</summary>
    private static async Task<IResult> RegisterAsync(
        [FromBody] CredentialsRequest request,
        HttpContext context,
        [FromServices] AuthClient auth,
        [FromServices] SessionManager sessions,
        CancellationToken cancellationToken)
    {
        var registered = await auth.RegisterAsync(request.Email, request.Password, cancellationToken);
        if (!registered.IsSuccess)
        {
            return new AuthProblemResult(registered);
        }

        var signedIn = await auth.SignInAsync(request.Email, request.Password, SessionKind.Customer, cancellationToken);
        if (signedIn.Pair is null)
        {
            return new AuthProblemResult(signedIn);
        }

        var started = await sessions.StartAsync(context, SessionKind.Customer, signedIn.Pair);
        return NoStore(context, TypedResults.Created((string?)null, Describe(started.Session)));
    }

    /// <summary>The current session; refreshed first if needed, so new roles and permissions show.</summary>
    private static async Task<IResult> GetAsync(SessionKind kind, HttpContext context, SessionManager sessions)
    {
        var hadCookie = SessionCookies.Read(context, kind) is not null;
        if (await sessions.LoadAsync(context, kind) is not { } loaded)
        {
            return Ended(context, kind, hadCookie);
        }

        var outcome = await sessions.EnsureFreshAsync(loaded);
        switch (outcome.Status)
        {
            case RefreshStatus.Ended:
                SessionCookies.Clear(context, kind);
                return GatewayProblem.SessionExpired;
            case RefreshStatus.Unavailable:
                return GatewayProblem.AuthUnavailable;
        }

        await sessions.TouchAsync(loaded);
        return NoStore(context, TypedResults.Ok(Describe(outcome.Session!)));
    }

    private static async Task<IResult> SignOutAsync(SessionKind kind, HttpContext context, SessionManager sessions)
    {
        if (await sessions.LoadAsync(context, kind) is not { } loaded)
        {
            // Nothing to end; make sure the browser forgets the cookie anyway.
            SessionCookies.Clear(context, kind);
            return TypedResults.NoContent();
        }

        if (!RequestGuards.IsCsrfSafe(context, loaded.Session))
        {
            return GatewayProblem.CsrfInvalid;
        }

        await sessions.EndAsync(context, loaded);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Changes the password through the auth service, which ends every session of the user; this
    /// one continues with the pair it returns. Runs under the session's lock, so a concurrent
    /// refresh cannot use the revoked refresh token and end the session.
    /// </summary>
    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] PasswordChangeRequest request,
        HttpContext context,
        [FromServices] AuthClient auth,
        [FromServices] SessionManager sessions)
    {
        var hadCookie = SessionCookies.Read(context, SessionKind.Customer) is not null;
        if (await sessions.LoadAsync(context, SessionKind.Customer) is not { } loaded)
        {
            return Ended(context, SessionKind.Customer, hadCookie);
        }

        if (!RequestGuards.IsCsrfSafe(context, loaded.Session))
        {
            return GatewayProblem.CsrfInvalid;
        }

        var outcome = await sessions.EnsureFreshAsync(loaded);
        if (outcome.Status != RefreshStatus.Fresh)
        {
            return outcome.Status == RefreshStatus.Ended ? Ended(context, SessionKind.Customer, hadCookie: true) : GatewayProblem.AuthUnavailable;
        }

        // Not cancelled with the request: once the auth service has changed the password, the new
        // pair must reach the session, or the session is left with revoked tokens.
        return await sessions.ChangeAsync<IResult>(
            loaded,
            async current =>
            {
                var result = await auth.ChangePasswordAsync(current.AccessToken, request.CurrentPassword, request.NewPassword, CancellationToken.None);
                return result.Pair is null
                    ? (null, new AuthProblemResult(result))
                    : (SessionManager.WithTokens(current, result.Pair), TypedResults.NoContent());
            },
            whenGone: GatewayProblem.AuthUnavailable);
    }

    private static IResult Ended(HttpContext context, SessionKind kind, bool hadCookie)
    {
        if (!hadCookie)
        {
            return GatewayProblem.SessionRequired;
        }

        SessionCookies.Clear(context, kind);
        return GatewayProblem.SessionExpired;
    }

    private static SessionResponse Describe(BrowserSession session)
    {
        var token = new JsonWebToken(session.AccessToken);
        return new SessionResponse(
            session.UserId,
            token.GetClaim("email").Value,
            [.. token.Claims.Where(claim => claim.Type == "role").Select(claim => claim.Value)],
            [.. token.Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value)],
            session.CsrfToken);
    }

    private static IResult NoStore(HttpContext context, IResult result)
    {
        context.Response.Headers.CacheControl = "no-store";
        return result;
    }
}
