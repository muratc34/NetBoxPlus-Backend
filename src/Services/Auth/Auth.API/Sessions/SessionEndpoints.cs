using Auth.Application.EmailChange.RequestEmailChange;
using Auth.Application.Sessions;
using Auth.Application.Sessions.Devices;
using Auth.Application.Sessions.Refresh;
using Auth.Application.Sessions.SignIn;
using Auth.Application.Sessions.SignOut;
using Auth.Application.Users.ChangePassword;
using Auth.Application.Users.DeleteAccount;
using Auth.Application.Users.GetMe;
using Auth.Application.Users.Register;
using Auth.Domain.RefreshTokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Auth.API.Sessions;

public sealed record RegisterRequest(string Email, string Password);

/// <summary>The new account's id and its first session: the same fields as a sign-in, and the id.</summary>
public sealed record RegisterResponse(
    Guid Id,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid SessionId);

public sealed record SignInRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ChangeEmailRequest(string? Password, string? NewEmail);

public sealed record DeleteAccountRequest(string? Password);

/// <summary>
/// Accounts and sessions. The gateway calls these for the browser and keeps the tokens on its
/// side; other clients may call them directly. Responses that carry tokens are never cached.
/// </summary>
internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sessions = endpoints.MapGroup(string.Empty).WithTags("Sessions").AddEndpointFilter(NoStore);

        sessions.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithSummary("Creates an account and opens its first customer session.")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapPost("/sign-in", ([FromBody] SignInRequest request, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
                => SignInAsync(request, SessionKind.Customer, context, dispatcher, cancellationToken))
            .WithName("SignIn")
            .WithSummary("Opens a customer session.")
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Not under /admin: the gateway exposes the admin paths, and this one returns tokens, which
        // only the gateway itself may receive (it opens the admin panel's session with them).
        sessions.MapPost("/sign-in/admin", ([FromBody] SignInRequest request, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
                => SignInAsync(request, SessionKind.Admin, context, dispatcher, cancellationToken))
            .WithName("AdminSignIn")
            .WithSummary("Opens an admin session; requires the auth:admin:sign-in permission.")
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        sessions.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .WithSummary("Exchanges a refresh token for a new pair. Each refresh token works once.")
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        sessions.MapPost("/sign-out", SignOutAsync)
            .WithName("SignOut")
            .WithSummary("Ends the session of the refresh token.")
            .Produces(StatusCodes.Status204NoContent);

        sessions.MapGet("/me", GetMeAsync)
            .WithName("GetMe")
            .WithSummary("Returns the caller's account, roles and permissions.")
            .RequireAuthorization()
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        sessions.MapPost("/me/password", ChangePasswordAsync)
            .WithName("ChangePassword")
            .WithSummary("Changes the caller's password, ends all their sessions and opens a new one.")
            .RequireAuthorization()
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        sessions.MapPost("/me/email", ChangeEmailAsync)
            .WithName("RequestEmailChange")
            .WithSummary("Sends a link to the new address; the account moves there when the link is followed. Once a minute at most.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapGet("/me/sessions", GetSessionsAsync)
            .WithName("GetSessions")
            .WithSummary("The account's open sessions, one per sign-in, the most recently active first; the caller's own is marked.")
            .RequireAuthorization()
            .Produces<IReadOnlyList<DeviceSession>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        sessions.MapDelete("/me/sessions/{sessionId:guid}", EndSessionAsync)
            .WithName("EndSession")
            .WithSummary("Ends one of the account's sessions; it cannot be renewed any more.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        sessions.MapDelete("/me/sessions", EndOtherSessionsAsync)
            .WithName("EndOtherSessions")
            .WithSummary("Ends every session of the account but the caller's own; returns the ones that ended.")
            .RequireAuthorization()
            .Produces<EndedSessions>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        sessions.MapPost("/me/deletion", DeleteAccountAsync)
            .WithName("DeleteAccount")
            .WithSummary("Deletes the caller's account after checking the password; its sessions end and the other services delete their data. Cannot be undone.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        HttpContext context,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new RegisterCommand(request.Email, request.Password, DeviceOf(context)), cancellationToken);

        // No location: the account is read through /me with the session.
        return result.ToHttpResult(account => TypedResults.Created(
            (string?)null,
            new RegisterResponse(account.UserId, account.Tokens.AccessToken, account.Tokens.AccessTokenExpiresAt, account.Tokens.RefreshToken, account.Tokens.RefreshTokenExpiresAt, account.Tokens.SessionId)));
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        SessionKind kind,
        HttpContext context,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new SignInCommand(request.Email, request.Password, kind, DeviceOf(context)), cancellationToken)).ToHttpResult();

    /// <summary>
    /// The device a session is opened from, named from the <c>User-Agent</c>: the gateway passes on the browser's.
    /// Only the name is kept.
    /// </summary>
    internal static string? DeviceOf(HttpContext context) => DeviceLabels.FromUserAgent(context.Request.Headers.UserAgent);

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new RefreshCommand(request.RefreshToken), cancellationToken)).ToHttpResult();

    private static async Task<IResult> SignOutAsync(
        [FromBody] RefreshRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new SignOutCommand(request.RefreshToken), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetSessionsAsync(HttpContext context, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetSessionsQuery(currentUser.Id!.Value, SessionOf(context)), cancellationToken)).ToHttpResult();

    private static async Task<IResult> EndSessionAsync(Guid sessionId, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new EndSessionCommand(currentUser.Id!.Value, sessionId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> EndOtherSessionsAsync(HttpContext context, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new EndOtherSessionsCommand(currentUser.Id!.Value, SessionOf(context)), cancellationToken)).ToHttpResult();

    /// <summary>The caller's session from the access token's <c>sid</c>; none in a token issued before sessions had ids.</summary>
    private static Guid? SessionOf(HttpContext context)
        => Guid.TryParse(context.User.FindFirst("sid")?.Value, out var id) ? id : null;

    private static async Task<IResult> GetMeAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetMeQuery(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext context,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(
            new ChangePasswordCommand(currentUser.Id!.Value, request.CurrentPassword, request.NewPassword, DeviceOf(context)),
            cancellationToken)).ToHttpResult();

    private static async Task<IResult> ChangeEmailAsync(
        [FromBody] ChangeEmailRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(
            new RequestEmailChangeCommand(currentUser.Id!.Value, request.Password, request.NewEmail),
            cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAccountAsync(
        [FromBody] DeleteAccountRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeleteAccountCommand(currentUser.Id!.Value, request.Password), cancellationToken)).ToHttpResult();

    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return await next(context);
    }
}
