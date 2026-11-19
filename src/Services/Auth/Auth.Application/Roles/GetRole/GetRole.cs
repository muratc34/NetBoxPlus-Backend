using Auth.Application.Abstractions;
using Auth.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Roles.GetRole;

public sealed record GetRoleQuery(Guid RoleId) : IQuery<RoleDetails>;

public sealed record RoleDetails(
    Guid Id,
    string Name,
    bool IsSystem,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);

internal sealed class GetRoleHandler(IAuthReadModel readModel) : IQueryHandler<GetRoleQuery, RoleDetails>
{
    public async Task<Result<RoleDetails>> Handle(GetRoleQuery query, CancellationToken cancellationToken)
    {
        var role = await readModel.Roles
            .FirstOrDefaultAsync(role => role.Id == query.RoleId, cancellationToken)
            .ConfigureAwait(false);

        return role is null
            ? RoleErrors.NotFound
            : new RoleDetails(role.Id, role.Name, role.IsSystem, role.Permissions, role.CreatedAt, role.ModifiedAt);
    }
}
