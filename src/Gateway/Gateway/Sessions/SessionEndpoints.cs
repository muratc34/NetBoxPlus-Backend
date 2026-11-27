using System.Text.Json;
using Gateway.RateLimiting;
using Gateway.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Gateway.Sessions;

public sealed record CredentialsRequest(string? Email, string? Password);

public sealed record PasswordChangeRequest(string? CurrentPassword, string? NewPassword);

public sealed record EmailChangeRequest(string? Password, string? NewEmail);

public sealed record AccountDeletionRequest(string? Password);

public sealed record PasswordResetRequest(string? Email);

public sealed record PasswordResetConfirmRequest(string? Token, string? NewPassword);

/// <summary>
/// The signed-in user as the session's access token describes them, and the CSRF token for
/// changing requests. Tokens themselves never leave the gateway.
/// </summary>
/// <summary>One of the account's sessions (sign-ins), as the auth service lists them.</summary>
/// <param name="Kind">Customer or Admin.</param>
/// <param name="DeviceLabel">Such as "Chrome · Windows"; null when it is not known.</param>
/// <param name="Current">The session of the browser asking.</param>
public sealed record DeviceSessionResponse(Guid Id, string Kind, string? DeviceLabel, DateTimeOffset SignedInAt, DateTimeOffset LastActiveAt, bool Current);

/// <summary>The sessions that ended.</summary>
public sealed record EndedSessionsResponse(IReadOnlyList<Guid> Ids);

/// <param name="Credential">The browser's PublicKeyCredential, as its toJSON() gives it.</param>
public sealed record PasskeySignInRequest(Guid ChallengeId, JsonElement Credential);

public sealed record PasskeyPasswordRequest(string? Password);

/// <param name="Credential">The browser's PublicKeyCredential, as its toJSON() gives it.</param>
public sealed record AddPasskeyRequest(Guid ChallengeId, string? Name, JsonElement Credential);

public sealed record RenamePasskeyRequest(string? Name);

/// <param name="Options">The PublicKeyCredentialCreationOptions or …RequestOptions JSON, for the browser's parse…FromJSON.</param>
public sealed record PasskeyCeremonyResponse(Guid ChallengeId, JsonElement Options);

public sealed record PasskeyResponse(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record CreatedPasskeyResponse(Guid Id);

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
        var customer = endpoints.MapGroup("/session").RequireCors(RouteGroups.Public).WithTags("Session");
        MapCommon(customer, SessionKind.Customer, RouteGroups.Public, namePrefix: string.Empty);

        customer.MapPost("/register", RegisterAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("Register")
            .WithSummary("Creates a customer account and opens its session.")
            .Produces<SessionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        customer.MapPost("/password", ChangePasswordAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("ChangePassword")
            .WithSummary("Changes the password; this session continues, the account's other sessions end.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Takes the password and sends an e-mail: rate-limited like the password change.
        customer.MapPost("/email", RequestEmailChangeAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("RequestEmailChange")
            .WithSummary("Sends a link to the new address; the address changes when the link is opened.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Takes the password: rate-limited like the password change.
        customer.MapPost("/deletion", DeleteAccountAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("DeleteAccount")
            .WithSummary("Deletes the account for good; the session ends and the cookie is cleared.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        customer.MapGet("/devices", GetDevicesAsync)
            .WithName("GetDevices")
            .WithSummary("The account's open sessions, one per sign-in, the most recently active first; this browser's is marked.")
            .Produces<IReadOnlyList<DeviceSessionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        customer.MapDelete("/devices/{sessionId:guid}", EndDeviceAsync)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("EndDevice")
            .WithSummary("Signs one of the account's sessions out at once; this browser's own works like signing out.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        customer.MapDelete("/devices", EndOtherDevicesAsync)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("EndOtherDevices")
            .WithSummary("Signs every other session of the account out at once; this one stays.")
            .Produces<EndedSessionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // No session and no cookie: the browser of someone who forgot the password. Rate-limited like
        // sign-in, since one sends e-mails and the other sets a password.
        customer.MapPost("/password-reset", RequestPasswordResetAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("RequestPasswordReset")
            .WithSummary("Sends a reset link if the address has an account; the answer is the same either way.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        customer.MapPost("/password-reset/confirm", ResetPasswordAsync)
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(RouteGroups.Public, next))
            .WithName("ResetPassword")
            .WithSummary("Sets a new password with the link's token; every session of the account ends.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        var admin = endpoints.MapGroup("/admin/session").RequireCors(RouteGroups.Admin).WithTags("Admin session");
        MapCommon(admin, SessionKind.Admin, RouteGroups.Admin, namePrefix: "Admin");

        return endpoints;
    }

    private static void MapCommon(RouteGroupBuilder group, SessionKind kind, string routeGroup, string namePrefix)
    {
        group.MapPost("/sign-in", (
                [FromBody] CredentialsRequest request,
                HttpContext context,
                [FromServices] AuthClient auth,
                [FromServices] SessionManager sessions,
                CancellationToken cancellationToken) => SignInAsync(request, kind, context, auth, sessions, cancellationToken))
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "SignIn")
            .WithSummary("Signs in and opens a session; the browser gets the session cookie.")
            .Produces<SessionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", (HttpContext context, [FromServices] SessionManager sessions) => GetAsync(kind, context, sessions))
            .RequireRateLimiting(routeGroup)
            .WithName(namePrefix + "GetSession")
            .WithSummary("The signed-in user and the CSRF token for changing requests.")
            .Produces<SessionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/sign-out", (HttpContext context, [FromServices] SessionManager sessions) => SignOutAsync(kind, context, sessions))
            .RequireRateLimiting(routeGroup)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "SignOut")
            .WithSummary("Ends the session and clears the cookie.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        MapPasskeys(group, kind, routeGroup, namePrefix);
    }

    /// <summary>Signing in with a passkey, and the account's passkeys; the same for both applications.</summary>
    private static void MapPasskeys(RouteGroupBuilder group, SessionKind kind, string routeGroup, string namePrefix)
    {
        // Anyone may ask; it is limited like a sign-in, since each answer keeps a challenge for five minutes.
        group.MapPost("/passkey/options", async ([FromServices] AuthClient auth, HttpContext context)
                => NoStore(context, new AuthJsonResult(await auth.StartPasskeySignInAsync(context.RequestAborted))))
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "StartPasskeySignIn")
            .WithSummary("Options for signing in with a passkey (navigator.credentials.get); they work once, for five minutes.")
            .Produces<PasskeyCeremonyResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/passkey", async (
                [FromBody] PasskeySignInRequest request,
                HttpContext context,
                [FromServices] AuthClient auth,
                [FromServices] SessionManager sessions) =>
            {
                var result = await auth.PasskeySignInAsync(request, kind, context.Request.Headers.UserAgent, context.RequestAborted);
                if (result.Pair is null)
                {
                    return (IResult)new AuthProblemResult(result);
                }

                var started = await sessions.StartAsync(context, kind, result.Pair);
                return NoStore(context, TypedResults.Ok(Describe(started.Session)));
            })
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "PasskeySignIn")
            .WithSummary("Signs in with the passkey that answered the options and opens a session; the browser gets the session cookie.")
            .Produces<SessionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/passkeys", (HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
                => WithFreshSessionAsync(context, sessions, kind, async (_, fresh) =>
                    new AuthJsonResult(await auth.PasskeysAsync(HttpMethod.Get, string.Empty, fresh.AccessToken, null, context.RequestAborted))))
            .RequireRateLimiting(routeGroup)
            .WithName(namePrefix + "GetPasskeys")
            .WithSummary("The account's passkeys, oldest first.")
            .Produces<IReadOnlyList<PasskeyResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Takes the password: limited like the password change.
        group.MapPost("/passkeys/options", ([FromBody] PasskeyPasswordRequest request, HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
                => WithFreshSessionAsync(context, sessions, kind, async (_, fresh) =>
                    new AuthJsonResult(await auth.PasskeysAsync(HttpMethod.Post, "/options", fresh.AccessToken, request, context.RequestAborted))))
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.CredentialsPolicy)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "StartPasskeyRegistration")
            .WithSummary("Checks the password again and returns the options for creating a passkey (navigator.credentials.create).")
            .Produces<PasskeyCeremonyResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/passkeys", ([FromBody] AddPasskeyRequest request, HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
                => WithFreshSessionAsync(context, sessions, kind, async (_, fresh) =>
                    new AuthJsonResult(await auth.PasskeysAsync(HttpMethod.Post, string.Empty, fresh.AccessToken, request, CancellationToken.None))))
            .RequireRateLimiting(routeGroup)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "AddPasskey")
            .WithSummary("Adds the passkey the browser created for the options.")
            .Produces<CreatedPasskeyResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/passkeys/{passkeyId:guid}", (Guid passkeyId, [FromBody] RenamePasskeyRequest request, HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
                => WithFreshSessionAsync(context, sessions, kind, async (_, fresh) =>
                    new AuthJsonResult(await auth.PasskeysAsync(HttpMethod.Put, $"/{passkeyId}", fresh.AccessToken, request, context.RequestAborted))))
            .RequireRateLimiting(routeGroup)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "RenamePasskey")
            .WithSummary("Renames one of the account's passkeys.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/passkeys/{passkeyId:guid}", (Guid passkeyId, HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
                => WithFreshSessionAsync(context, sessions, kind, async (_, fresh) =>
                    new AuthJsonResult(await auth.PasskeysAsync(HttpMethod.Delete, $"/{passkeyId}", fresh.AccessToken, null, CancellationToken.None))))
            .RequireRateLimiting(routeGroup)
            .AddEndpointFilterFactory((_, next) => RequestGuards.RequireAllowedOrigin(routeGroup, next))
            .WithName(namePrefix + "RemovePasskey")
            .WithSummary("Removes one of the account's passkeys.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> SignInAsync(
        CredentialsRequest request,
        SessionKind kind,
        HttpContext context,
        AuthClient auth,
        SessionManager sessions,
        CancellationToken cancellationToken)
    {
        var result = await auth.SignInAsync(request.Email, request.Password, kind, context.Request.Headers.UserAgent, cancellationToken);
        if (result.Pair is null)
        {
            return new AuthProblemResult(result);
        }

        var started = await sessions.StartAsync(context, kind, result.Pair);
        return NoStore(context, TypedResults.Ok(Describe(started.Session)));
    }

    /// <summary>
    /// Creates the account and opens its session: one call for the customer application, and one to
    /// the auth service, which returns the first token pair with the account.
    /// </summary>
    private static async Task<IResult> RegisterAsync(
        [FromBody] CredentialsRequest request,
        HttpContext context,
        [FromServices] AuthClient auth,
        [FromServices] SessionManager sessions,
        CancellationToken cancellationToken)
    {
        var registered = await auth.RegisterAsync(request.Email, request.Password, context.Request.Headers.UserAgent, cancellationToken);
        if (registered.Pair is null)
        {
            return new AuthProblemResult(registered);
        }

        var started = await sessions.StartAsync(context, SessionKind.Customer, registered.Pair);
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
                var result = await auth.ChangePasswordAsync(current.AccessToken, request.CurrentPassword, request.NewPassword, context.Request.Headers.UserAgent, CancellationToken.None);
                return result.Pair is null
                    ? (null, new AuthProblemResult(result))
                    : (SessionManager.WithTokens(current, result.Pair), TypedResults.NoContent());
            },
            whenGone: GatewayProblem.AuthUnavailable);
    }

    /// <summary>Passes the auth service's answer on: 202 whether or not the address has an account.</summary>
    /// <summary>
    /// Asks the auth service to send a change link to the new address; the session stays as it is,
    /// since the address is not part of it. The answer is passed on as it is.
    /// </summary>
    private static async Task<IResult> RequestEmailChangeAsync(
        [FromBody] EmailChangeRequest request,
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

        return new AuthProblemResult(await auth.RequestEmailChangeAsync(outcome.Session!.AccessToken, request.Password, request.NewEmail, context.RequestAborted));
    }

    /// <summary>
    /// Asks the auth service to delete the account; when it did, the session goes and the cookie is
    /// cleared. Not cancelled with the request: once the account is gone, the session must go too.
    /// </summary>
    private static async Task<IResult> DeleteAccountAsync(
        [FromBody] AccountDeletionRequest request,
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

        var result = await auth.DeleteAccountAsync(outcome.Session!.AccessToken, request.Password, CancellationToken.None);
        if (result.IsSuccess)
        {
            await sessions.ForgetAsync(context, loaded);
        }

        return new AuthProblemResult(result);
    }

    private static async Task<IResult> RequestPasswordResetAsync(
        [FromBody] PasswordResetRequest request,
        [FromServices] AuthClient auth,
        CancellationToken cancellationToken)
        => new AuthProblemResult(await auth.RequestPasswordResetAsync(request.Email, cancellationToken));

    /// <summary>
    /// Sets the new password; the auth service ends every session of the account, and the browser
    /// signs in again. A session cookie this browser may hold is left alone: it ends at its next refresh.
    /// </summary>
    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] PasswordResetConfirmRequest request,
        [FromServices] AuthClient auth,
        CancellationToken cancellationToken)
        => new AuthProblemResult(await auth.ResetPasswordAsync(request.Token, request.NewPassword, cancellationToken));

    /// <summary>The account's sessions, as the auth service lists them.</summary>
    private static Task<IResult> GetDevicesAsync(HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
        => WithFreshSessionAsync(context, sessions, async (_, fresh) => new AuthJsonResult(await auth.GetSessionsAsync(fresh.AccessToken, context.RequestAborted)));

    /// <summary>
    /// Ends one session in the auth service, then forgets the browser session using it: that browser is signed out at
    /// its next request. Ending this browser's own clears its cookie too. Not cancelled with the request: once the
    /// auth service has ended it, the browser session must go.
    /// </summary>
    private static Task<IResult> EndDeviceAsync(Guid sessionId, HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
        => WithFreshSessionAsync(context, sessions, async (loaded, fresh) =>
        {
            var result = await auth.EndSessionAsync(fresh.AccessToken, sessionId, CancellationToken.None);
            if (result.IsSuccess)
            {
                await sessions.ForgetSignInAsync(sessionId);
                if (fresh.SessionId == sessionId)
                {
                    await sessions.ForgetAsync(context, loaded);
                }
            }

            return new AuthJsonResult(result);
        });

    /// <summary>Ends every other session in the auth service, then forgets the browser sessions using them.</summary>
    private static Task<IResult> EndOtherDevicesAsync(HttpContext context, [FromServices] AuthClient auth, [FromServices] SessionManager sessions)
        => WithFreshSessionAsync(context, sessions, async (loaded, fresh) =>
        {
            var result = await auth.EndOtherSessionsAsync(fresh.AccessToken, CancellationToken.None);
            if (result.IsSuccess && result.Body is { } body
                && JsonSerializer.Deserialize<EndedSessionsResponse>(body, JsonSerializerOptions.Web) is { } ended)
            {
                foreach (var id in ended.Ids)
                {
                    await sessions.ForgetSignInAsync(id);
                }

                // Without an id of its own (a session from before ids), this one ended with the others.
                if (fresh.SessionId is not { } own || ended.Ids.Contains(own))
                {
                    await sessions.ForgetAsync(context, loaded);
                }
            }

            return new AuthJsonResult(result);
        });

    /// <summary>
    /// The common start of a request on the customer's own account: a session, the CSRF token for a change, and an
    /// access token that is not about to run out.
    /// </summary>
    private static Task<IResult> WithFreshSessionAsync(
        HttpContext context,
        SessionManager sessions,
        Func<LoadedSession, BrowserSession, Task<IResult>> action)
        => WithFreshSessionAsync(context, sessions, SessionKind.Customer, action);

    private static async Task<IResult> WithFreshSessionAsync(
        HttpContext context,
        SessionManager sessions,
        SessionKind kind,
        Func<LoadedSession, BrowserSession, Task<IResult>> action)
    {
        var hadCookie = SessionCookies.Read(context, kind) is not null;
        if (await sessions.LoadAsync(context, kind) is not { } loaded)
        {
            return Ended(context, kind, hadCookie);
        }

        if (!RequestGuards.IsCsrfSafe(context, loaded.Session))
        {
            return GatewayProblem.CsrfInvalid;
        }

        var outcome = await sessions.EnsureFreshAsync(loaded);
        if (outcome.Status != RefreshStatus.Fresh)
        {
            return outcome.Status == RefreshStatus.Ended ? Ended(context, kind, hadCookie: true) : GatewayProblem.AuthUnavailable;
        }

        return NoStore(context, await action(loaded, outcome.Session!));
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
