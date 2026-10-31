using Auth.Application.Roles.CreateRole;
using Auth.Application.Roles.DeleteRole;
using Auth.Application.Roles.GetRole;
using Auth.Application.Roles.GetRoles;
using Auth.Application.Roles.UpdateRole;
using Auth.Domain.Roles;
using Microsoft.AspNetCore.Mvc;
using Shared.Application.Cqrs;
using Shared.Infrastructure.API.Errors;
using Shared.Infrastructure.API.Security;

namespace Auth.API.Admin;

/// <param name="Permissions">Names from the catalog (<c>GET /admin/permissions</c>); missing means none.</param>
public sealed record RoleRequest(string Name, IReadOnlyList<string>? Permissions);

public sealed record CreateRoleResponse(Guid Id);

/// <summary>
/// Roles and the permission catalog, for the admin panel. A caller can only create, change, delete
/// or hand out a role whose permissions they hold themselves. Every change is written to the audit log.
/// </summary>
internal static class RoleAdminEndpoints
{
    public static IEndpointRouteBuilder MapRoleAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var roles = endpoints.MapGroup("/admin/roles").WithTags("Roles");

        roles.MapGet("/", GetRolesAsync)
            .WithName("GetRoles")
            .WithSummary("Lists every role with its permissions and how many users hold it.")
            .RequirePermission(Permissions.Auth.RolesRead)
            .Produces<IReadOnlyList<RoleSummary>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        roles.MapGet("/{id:guid}", GetRoleAsync)
            .WithName("GetRole")
            .WithSummary("Returns a role.")
            .RequirePermission(Permissions.Auth.RolesRead)
            .Produces<RoleDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        roles.MapPost("/", CreateAsync)
            .WithName("CreateRole")
            .WithSummary("Creates a role.")
            .RequirePermission(Permissions.Auth.RolesWrite)
            .Produces<CreateRoleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateRole")
            .WithSummary("Replaces the role's name and permissions. The administrator role cannot be changed.")
            .RequirePermission(Permissions.Auth.RolesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        roles.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteRole")
            .WithSummary("Deletes the role and takes it away from its holders. The administrator role cannot be deleted.")
            .RequirePermission(Permissions.Auth.RolesWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        endpoints.MapGet("/admin/permissions", GetPermissions)
            .WithTags("Roles")
            .WithName("GetPermissions")
            .WithSummary("Lists every permission a role can hold.")
            .RequirePermission(Permissions.Auth.RolesRead)
            .Produces<IReadOnlyList<string>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> GetRolesAsync([FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetRolesQuery(), cancellationToken)).ToHttpResult();

    private static async Task<IResult> GetRoleAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Query(new GetRoleQuery(id), cancellationToken)).ToHttpResult();

    private static async Task<IResult> CreateAsync(
        [FromBody] RoleRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new CreateRoleCommand(request.Name, request.Permissions ?? []), cancellationToken);

        return result.ToHttpResult(id => TypedResults.Created($"/admin/roles/{id}", new CreateRoleResponse(id)));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] RoleRequest request,
        [FromServices] IDispatcher dispatcher,
        CancellationToken cancellationToken)
        => (await dispatcher.Send(new UpdateRoleCommand(id, request.Name, request.Permissions ?? []), cancellationToken)).ToHttpResult();

    private static async Task<IResult> DeleteAsync(Guid id, [FromServices] IDispatcher dispatcher, CancellationToken cancellationToken)
        => (await dispatcher.Send(new DeleteRoleCommand(id), cancellationToken)).ToHttpResult();

    private static IResult GetPermissions()
        => TypedResults.Ok<IReadOnlyList<string>>([.. Permissions.All.Order(StringComparer.Ordinal)]);
}
