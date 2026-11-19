using Auth.Application.EmailVerification.ConfirmEmail;
using Auth.Application.EmailVerification.GetVerificationStatus;
using Auth.Application.EmailVerification.ResendVerification;
using Microsoft.AspNetCore.Mvc;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Auth.API.Sessions;

public sealed record ConfirmEmailRequest(string? Token);

/// <summary>
/// E-mail verification. The gateway exposes these to the browser (<c>/auth/email-verification/...</c>);
/// neither returns a token.
/// </summary>
internal static class EmailVerificationEndpoints
{
    public static IEndpointRouteBuilder MapEmailVerificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var verification = endpoints.MapGroup("/email-verification").WithTags("EmailVerification");

        verification.MapGet(string.Empty, GetStatusAsync)
            .WithName("GetEmailVerification")
            .WithSummary("Tells whether the caller's e-mail address is verified.")
            .RequireAuthorization()
            .Produces<VerificationStatus>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        verification.MapPost(string.Empty, ResendAsync)
            .WithName("ResendEmailVerification")
            .WithSummary("Sends the caller a new verification link; earlier links stop working. Once a minute at most.")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        verification.MapPost("/confirm", ConfirmAsync)
            .WithName("ConfirmEmail")
            .WithSummary("Verifies the e-mail address with the token from the link. Needs no sign-in; each token works once.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetVerificationStatusQuery(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ResendAsync(
        [FromServices] ICurrentUser currentUser,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new ResendVerificationCommand(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> ConfirmAsync(
        [FromBody] ConfirmEmailRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new ConfirmEmailCommand(request.Token), cancellationToken)).ToHttpResult();
}
