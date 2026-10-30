using Auth.Application.Sessions;
using Auth.Application.Sessions.Refresh;
using Auth.Application.Sessions.SignIn;
using Auth.Application.Sessions.SignOut;
using Auth.Application.Users.ChangePassword;
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

public sealed record RegisterResponse(Guid Id);

public sealed record SignInRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

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
            .WithSummary("Creates an account. Sign in afterwards.")
            .Produces<RegisterResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        sessions.MapPost("/sign-in", ([FromBody] SignInRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
                => SignInAsync(request, SessionKind.Customer, dispatcher, cancellationToken))
            .WithName("SignIn")
            .WithSummary("Opens a customer session.")
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Under /admin so that it is reachable through the gateway's admin routes only.
        sessions.MapPost("/admin/sign-in", ([FromBody] SignInRequest request, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
                => SignInAsync(request, SessionKind.Admin, dispatcher, cancellationToken))
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

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new RegisterCommand(request.Email, request.Password), cancellationToken);

        // No location: the account is read through /me once signed in.
        return result.ToHttpResult(id => TypedResults.Created((string?)null, new RegisterResponse(id)));
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        SessionKind kind,
        IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new SignInCommand(request.Email, request.Password, kind), cancellationToken)).ToHttpResult();

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

    private static async Task<IResult> GetMeAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetMeQuery(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(
            new ChangePasswordCommand(currentUser.Id!.Value, request.CurrentPassword, request.NewPassword),
            cancellationToken)).ToHttpResult();

    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";
        return await next(context);
    }
}
