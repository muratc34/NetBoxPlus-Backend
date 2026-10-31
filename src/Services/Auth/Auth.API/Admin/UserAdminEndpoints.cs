using Auth.Application.Users.AssignRole;
using Auth.Application.Users.DisableUser;
using Auth.Application.Users.EnableUser;
using Auth.Application.Users.GetUser;
using Auth.Application.Users.GetUsers;
using Auth.Application.Users.RemoveRole;
using Auth.Domain.Roles;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Application.Paging;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Auth.API.Admin;

/// <summary>Accounts, for the admin panel. Every change is written to the audit log.</summary>
internal static class UserAdminEndpoints
{
    public static IEndpointRouteBuilder MapUserAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/admin/users").WithTags("Users");

        users.MapGet("/", GetUsersAsync)
            .WithName("GetUsers")
            .WithSummary("Lists accounts, newest first; optionally only those whose e-mail address contains the search text.")
            .RequirePermission(Permissions.Auth.UsersRead)
            .Produces<Page<UserSummary>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        users.MapGet("/{id:guid}", GetUserAsync)
            .WithName("GetUser")
            .WithSummary("Returns an account with its roles and lock state.")
            .RequirePermission(Permissions.Auth.UsersRead)
            .Produces<UserDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPost("/{id:guid}/disable", DisableAsync)
            .WithName("DisableUser")
            .WithSummary("Stops the account from signing in and ends its sessions. Not allowed on your own account.")
            .RequirePermission(Permissions.Auth.UsersWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        users.MapPost("/{id:guid}/enable", EnableAsync)
            .WithName("EnableUser")
            .WithSummary("Lets the account sign in again and lifts a lockout after failed sign-ins.")
            .RequirePermission(Permissions.Auth.UsersWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapPut("/{id:guid}/roles/{roleId:guid}", AssignRoleAsync)
            .WithName("AssignRole")
            .WithSummary("Gives the account a role. Requires holding every permission of the role.")
            .RequirePermission(Permissions.Auth.UsersWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        users.MapDelete("/{id:guid}/roles/{roleId:guid}", RemoveRoleAsync)
            .WithName("RemoveRole")
            .WithSummary("Takes a role away from the account. Requires holding every permission of the role; not allowed on your own account.")
            .RequirePermission(Permissions.Auth.UsersWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetUsersAsync(
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageLimits.DefaultPageSize)
        => (await dispatcher.Query(new GetUsersQuery(search, page, pageSize), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetUserAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetUserQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DisableAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DisableUserCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> EnableAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new EnableUserCommand(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> AssignRoleAsync(
        Guid id,
        Guid roleId,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new AssignRoleCommand(id, roleId), cancellationToken)).ToHttpResult();

    private static async Task<IResult> RemoveRoleAsync(
        Guid id,
        Guid roleId,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new RemoveRoleCommand(id, roleId), cancellationToken)).ToHttpResult();
}
