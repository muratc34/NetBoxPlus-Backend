using Auth.Application.EmailChange.ConfirmEmailChange;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Auth.API.Sessions;

public sealed record ConfirmEmailChangeRequest(string? Token);

/// <summary>
/// Confirms an e-mail change with the link sent to the new address. The gateway exposes it to the
/// browser (<c>/auth/email-change/...</c>); it returns no token. The change itself is asked for with
/// <c>POST /me/email</c>, which needs the password and goes through the gateway's session.
/// </summary>
internal static class EmailChangeEndpoints
{
    public static IEndpointRouteBuilder MapEmailChangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/email-change").WithTags("EmailChange")
            .MapPost("/confirm", ConfirmAsync)
            .WithName("ConfirmEmailChange")
            .WithSummary("Moves the account to the address the link was sent to. Needs no sign-in; each token works once.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ConfirmAsync(
        [FromBody] ConfirmEmailChangeRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new ConfirmEmailChangeCommand(request.Token), cancellationToken)).ToHttpResult();
}
