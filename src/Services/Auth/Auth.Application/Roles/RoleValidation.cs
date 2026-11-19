using Auth.Domain.Roles;
using Shared.Application.Validation;

namespace Auth.Application.Roles;

/// <summary>The input checks shared by creating and changing a role; the role checks them again.</summary>
internal static class RoleValidation
{
    public static IEnumerable<ValidationFailure> Validate(string? name, IReadOnlyList<string>? permissions)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            yield return new ValidationFailure("Name", RoleErrors.NameRequired.Code, RoleErrors.NameRequired.Message);
        }
        else if (name.Trim().Length > Role.NameMaxLength)
        {
            yield return new ValidationFailure("Name", RoleErrors.NameTooLong.Code, RoleErrors.NameTooLong.Message);
        }

        foreach (var permission in permissions ?? [])
        {
            if (!Permissions.IsKnown(permission))
            {
                var error = RoleErrors.UnknownPermission(permission);
                yield return new ValidationFailure("Permissions", error.Code, error.Message);
            }
        }
    }
}
