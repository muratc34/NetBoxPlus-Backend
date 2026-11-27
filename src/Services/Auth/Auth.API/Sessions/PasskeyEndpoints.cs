using System.Text.Json;
using Auth.Application.Passkeys;
using Auth.Application.Sessions;
using Auth.Application.Sessions.PasskeySignIn;
using Auth.Domain.RefreshTokens;
using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Auth.API.Sessions;

public sealed record PasskeyOptionsRequest(string Password);

/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as its <c>toJSON()</c> gives it.</param>
public sealed record AddPasskeyRequest(Guid ChallengeId, string? Name, JsonElement Credential);

public sealed record RenamePasskeyRequest(string? Name);

/// <param name="Credential">The browser's <c>PublicKeyCredential</c>, as its <c>toJSON()</c> gives it.</param>
public sealed record PasskeySignInRequest(Guid ChallengeId, JsonElement Credential);

public sealed record CreatedPasskey(Guid Id);

/// <summary>
/// Passkeys (WebAuthn): adding and managing them on the account, and signing in with them. The gateway calls
/// these for the browser; the sign-ins return tokens, which only the gateway receives.
/// </summary>
internal static class PasskeyEndpoints
{
    public static IEndpointRouteBuilder MapPasskeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var mine = endpoints.MapGroup("/me/passkeys").WithTags("Passkeys").RequireAuthorization().AddEndpointFilter(NoStore);

        mine.MapGet(string.Empty, GetAsync)
            .WithName("GetPasskeys")
            .WithSummary("The account's passkeys, oldest first.")
            .Produces<IReadOnlyList<PasskeySummary>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        mine.MapPost("/options", StartRegistrationAsync)
            .WithName("StartPasskeyRegistration")
            .WithSummary("Checks the password again and returns the options for creating a passkey; they work once, for five minutes.")
            .Produces<PasskeyCeremonyStart>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mine.MapPost(string.Empty, AddAsync)
            .WithName("AddPasskey")
            .WithSummary("Adds the passkey the browser created for the options; the user is told by e-mail.")
            .Produces<CreatedPasskey>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        mine.MapPut("/{passkeyId:guid}", RenameAsync)
            .WithName("RenamePasskey")
            .WithSummary("Renames one of the account's passkeys.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        mine.MapDelete("/{passkeyId:guid}", RemoveAsync)
            .WithName("RemovePasskey")
            .WithSummary("Removes one of the account's passkeys; the user is told by e-mail.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var signIn = endpoints.MapGroup("/sign-in/passkey").WithTags("Sessions").AddEndpointFilter(NoStore);

        signIn.MapPost("/options", StartSignInAsync)
            .WithName("StartPasskeySignIn")
            .WithSummary("Options for signing in with a passkey; they work once, for five minutes.")
            .Produces<PasskeyCeremonyStart>();

        signIn.MapPost(string.Empty, (PasskeySignInRequest request, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
                => SignInAsync(request, SessionKind.Customer, context, dispatcher, cancellationToken))
            .WithName("PasskeySignIn")
            .WithSummary("Opens a customer session with the passkey that signed the options.")
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        signIn.MapPost("/admin", (PasskeySignInRequest request, HttpContext context, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
                => SignInAsync(request, SessionKind.Admin, context, dispatcher, cancellationToken))
            .WithName("AdminPasskeySignIn")
            .WithSummary("Opens an admin session with the passkey; requires the auth:admin:sign-in permission.")
            .Produces<TokenPair>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> GetAsync([FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetPasskeysQuery(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> StartRegistrationAsync(PasskeyOptionsRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new StartPasskeyRegistrationCommand(currentUser.Id!.Value, request.Password), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AddAsync(AddPasskeyRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new FinishPasskeyRegistrationCommand(currentUser.Id!.Value, request.ChallengeId, request.Name, request.Credential.GetRawText()), cancellationToken))
            .ToHttpResult(id => TypedResults.Created((string?)null, new CreatedPasskey(id)));

    private static async Task<IResult> RenameAsync(Guid passkeyId, RenamePasskeyRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RenamePasskeyCommand(currentUser.Id!.Value, passkeyId, request.Name), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveAsync(Guid passkeyId, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemovePasskeyCommand(currentUser.Id!.Value, passkeyId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> StartSignInAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new StartPasskeySignInCommand(), cancellationToken)).ToHttpResult();

    private static async Task<IResult> SignInAsync(PasskeySignInRequest request, SessionKind kind, HttpContext context, IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(
            new FinishPasskeySignInCommand(request.ChallengeId, request.Credential.GetRawText(), kind, SessionEndpoints.DeviceOf(context)),
            cancellationToken)).ToHttpResult();

    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return await next(context).ConfigureAwait(false);
    }
}
