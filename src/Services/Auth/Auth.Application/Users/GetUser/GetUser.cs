using Auth.Application.Abstractions;
using Auth.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shared.Application.Cqrs;
using Shared.Domain;

namespace Auth.Application.Users.GetUser;

public sealed record GetUserQuery(Guid UserId) : IQuery<UserDetails>;

public sealed record UserDetails(
    Guid Id,
    string Email,
    bool IsDisabled,
    DateTimeOffset? LockedOutUntil,
    int FailedSignInCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    IReadOnlyList<UserRoleReference> Roles);

public sealed record UserRoleReference(Guid Id, string Name);

internal sealed class GetUserHandler(IAuthReadModel readModel) : IQueryHandler<GetUserQuery, UserDetails>
{
    public async Task<Result<UserDetails>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await readModel.Users
            .Include(user => user.Roles)
            .FirstOrDefaultAsync(user => user.Id == query.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var roleIds = user.Roles.Select(role => role.RoleId).ToList();
        var roles = await readModel.Roles
            .Where(role => roleIds.Contains(role.Id))
            .OrderBy(role => role.Name)
            .Select(role => new UserRoleReference(role.Id, role.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UserDetails(
            user.Id,
            user.Email.Value,
            user.IsDisabled,
            user.LockedOutUntil,
            user.FailedSignInCount,
            user.CreatedAt,
            user.ModifiedAt,
            roles);
    }
}
