using Microsoft.AspNetCore.Mvc;
using Notification.Application.SignIns;
using Shared.Application;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;

namespace Notification.API.Preferences;

public sealed record NotificationPreferencesRequest(bool SignInAlerts);

/// <summary>What the signed-in user wants to be told, and turning the alerts off from an e-mail without signing in.</summary>
internal static class PreferenceEndpoints
{
    public static IEndpointRouteBuilder MapPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var preferences = endpoints.MapGroup("/me/notification-preferences").WithTags("Preferences").RequireAuthorization();

        preferences.MapGet(string.Empty, GetAsync)
            .WithName("GetNotificationPreferences")
            .WithSummary("The caller's e-mail preferences; an alert is on until turned off.")
            .Produces<NotificationPreferencesView>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        preferences.MapPut(string.Empty, SetAsync)
            .WithName("SetNotificationPreferences")
            .WithSummary("Turns the caller's e-mail alerts on or off. E-mails about the account itself cannot be turned off.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // RFC 8058: a mail program posts "List-Unsubscribe=One-Click" to the address in the List-Unsubscribe header,
        // without cookies; the web app's confirmation page posts to the same address. The token is all it needs.
        endpoints.MapPost("/unsubscribe", UnsubscribeAsync)
            .WithTags("Preferences")
            .AllowAnonymous()
            .WithName("Unsubscribe")
            .WithSummary("Turns the sign-in alerts off with the token of an alert's link; works again and again until it expires.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetAsync([FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetNotificationPreferencesQuery(currentUser.Id!.Value), cancellationToken)).ToHttpResult();

    private static async Task<IResult> SetAsync([FromBody] NotificationPreferencesRequest request, [FromServices] ICurrentUser currentUser, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new SetNotificationPreferencesCommand(currentUser.Id!.Value, request.SignInAlerts), cancellationToken)).ToHttpResult();

    private static async Task<IResult> UnsubscribeAsync([FromQuery] string? token, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new UnsubscribeCommand(token), cancellationToken)).ToHttpResult();
}
