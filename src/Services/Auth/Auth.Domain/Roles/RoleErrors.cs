using Shared.Domain;

namespace Auth.Domain.Roles;

public static class RoleErrors
{
    public static readonly Error NameRequired = Error.Validation("Role.NameRequired", "The role name is required.");

    public static readonly Error NameTooLong = Error.Validation(
        "Role.NameTooLong",
        $"The role name must be at most {Role.NameMaxLength} characters.");

    public static readonly Error NotFound = Error.NotFound("Role.NotFound", "The role was not found.");

    public static Error UnknownPermission(string permission)
        => Error.Validation("Role.UnknownPermission", $"'{permission}' is not a known permission.");
}
