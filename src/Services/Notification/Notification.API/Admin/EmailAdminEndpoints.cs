using Microsoft.AspNetCore.Mvc;
using Notification.Application.Emails.GetEmails;
using Notification.Application.Emails.RetryEmail;
using Notification.Domain.Emails;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Notification.API.Admin;

internal static class NotificationPermissions
{
    public const string EmailsRead = "notification:emails:read";
    public const string EmailsWrite = "notification:emails:write";
}

/// <summary>The e-mails sent to users, for the admin panel: follow them, and send a failed one again.</summary>
internal static class EmailAdminEndpoints
{
    public static IEndpointRouteBuilder MapEmailAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/emails").WithTags("Emails");

        group.MapGet("/", GetEmailsAsync)
            .WithName("GetEmails")
            .WithSummary("Lists e-mails, newest first; without addresses.")
            .RequirePermission(NotificationPermissions.EmailsRead)
            .Produces<Page<EmailSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetEmailAsync)
            .WithName("GetEmail")
            .WithSummary("Returns an e-mail: its attempts, the last error, and whether its content is still kept.")
            .RequirePermission(NotificationPermissions.EmailsRead)
            .Produces<EmailDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/retry", RetryAsync)
            .WithName("RetryEmail")
            .WithSummary("Sends a failed e-mail again, from the first attempt.")
            .RequirePermission(NotificationPermissions.EmailsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetEmailsAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] EmailStatus? status = null,
        [FromQuery] Guid? userId = null,
        [FromQuery] EmailKind? kind = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetEmailsQuery(status, userId, kind, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetEmailAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetEmailQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RetryAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new RetryEmailCommand(id), cancellationToken)).ToHttpResult();
}
