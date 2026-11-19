using Auth.Domain.Roles;
using Shared.Application;
using Shared.Domain;

namespace Auth.Application.Roles;

/// <summary>
/// The rule that keeps an administrator from granting more than they have: a role can be created,
/// changed, deleted, given or taken away only by a caller who holds every permission involved.
/// Otherwise anyone allowed to manage users could give themselves the administrator role.
/// </summary>
internal static class CallerGrants
{
    /// <summary>Checked against the caller's access token, which is at most one token lifetime old.</summary>
    public static Result EnsureHolds(this ICurrentUser currentUser, IEnumerable<string> permissions)
    {
        var missing = permissions.FirstOrDefault(permission => !currentUser.HasPermission(permission));
        return missing is null ? Result.Success() : RoleErrors.PermissionNotHeld(missing);
    }
}
