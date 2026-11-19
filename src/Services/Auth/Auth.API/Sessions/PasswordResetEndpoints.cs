using Auth.Application.PasswordReset.RequestPasswordReset;
using Auth.Application.PasswordReset.ResetPassword;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Auth.API.Sessions;

public sealed record RequestPasswordResetRequest(string Email);

public sealed record ResetPasswordRequest(string? Token, string NewPassword);

/// <summary>
/// Resetting a forgotten password. The gateway exposes these to the browser through its own session
/// endpoints (<c>/session/password-reset/...</c>), rate-limited like sign-in; neither returns a token.
/// </summary>
internal static class PasswordResetEndpoints
{
    public static IEndpointRouteBuilder MapPasswordResetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reset = endpoints.MapGroup("/password-reset").WithTags("PasswordReset").AllowAnonymous();

        reset.MapPost(string.Empty, RequestAsync)
            .WithName("RequestPasswordReset")
            .WithSummary("E-mails a password reset link if the address has an account. Answers the same whether it has one or not.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        reset.MapPost("/confirm", ResetAsync)
            .WithName("ResetPassword")
            .WithSummary("Sets a new password with the token from the link and ends every session of the account. Each token works once.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> RequestAsync(
        [FromBody] RequestPasswordResetRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new RequestPasswordResetCommand(request.Email), cancellationToken);
        return result.IsSuccess ? TypedResults.Accepted((string?)null) : result.ToHttpResult();
    }

    private static async Task<IResult> ResetAsync(
        [FromBody] ResetPasswordRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken)).ToHttpResult();
}
