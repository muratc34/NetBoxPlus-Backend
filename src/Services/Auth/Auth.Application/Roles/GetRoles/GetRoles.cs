using Auth.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Roles.GetRoles;

/// <summary>Every role; there are few, so the list is not paged.</summary>
public sealed record GetRolesQuery : IQuery<IReadOnlyList<RoleSummary>>;

/// <param name="UserCount">How many users hold the role: who is affected by changing or deleting it.</param>
public sealed record RoleSummary(Guid Id, string Name, bool IsSystem, IReadOnlyCollection<string> Permissions, int UserCount);

internal sealed class GetRolesHandler(IAuthReadModel readModel) : IQueryHandler<GetRolesQuery, IReadOnlyList<RoleSummary>>
{
    public async Task<Result<IReadOnlyList<RoleSummary>>> Handle(GetRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await readModel.Roles
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userCounts = await readModel.Users
            .SelectMany(user => user.Roles)
            .GroupBy(userRole => userRole.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(count => count.RoleId, count => count.Count, cancellationToken)
            .ConfigureAwait(false);

        return roles
            .Select(role => new RoleSummary(role.Id, role.Name, role.IsSystem, role.Permissions, userCounts.GetValueOrDefault(role.Id)))
            .ToList();
    }
}
