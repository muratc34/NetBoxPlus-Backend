using Shared.Domain;

namespace Auth.Domain.Roles;

public static class RoleErrors
{
    public static readonly Error NameRequired = Error.Validation("Role.NameRequired", "The role name is required.");

    public static readonly Error NameTooLong = Error.Validation(
        "Role.NameTooLong",
        $"The role name must be at most {Role.NameMaxLength} characters.");

    public static readonly Error NotFound = Error.NotFound("Role.NotFound", "The role was not found.");

    public static readonly Error NameTaken = Error.Conflict("Role.NameTaken", "A role with this name already exists.");

    public static readonly Error SystemRole = Error.Conflict(
        "Role.SystemRole",
        "The administrator role always holds every permission and cannot be renamed, changed or deleted.");

    /// <summary>Changing a role, or giving it to someone, needs every permission the role holds or would hold.</summary>
    public static Error PermissionNotHeld(string permission)
        => Error.Forbidden("Role.PermissionNotHeld", $"You cannot grant or change '{permission}' because you do not hold it.");

    public static Error UnknownPermission(string permission)
        => Error.Validation("Role.UnknownPermission", $"'{permission}' is not a known permission.");
}
