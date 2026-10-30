using Shared.Domain;

namespace Auth.Domain.Roles;

/// <summary>
/// A named set of permissions. Users get roles; code checks permissions, never role names.
/// </summary>
public sealed class Role : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 64;

    private List<string> _permissions = [];

    private Role(Guid id, string name, List<string> permissions)
        : base(id)
    {
        Name = name;
        NormalizedName = Normalize(name);
        _permissions = permissions;
    }

    /// <summary>Used by EF Core when materializing.</summary>
    private Role()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>The name in upper case; unique, so "Editor" and "editor" cannot both exist.</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public IReadOnlyCollection<string> Permissions => _permissions.AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ModifiedAt { get; private set; }

    public static Result<Role> Create(string? name, IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var checkedName = CheckName(name);
        if (checkedName.IsFailure)
        {
            return checkedName.Error;
        }

        var checkedPermissions = CheckPermissions(permissions);
        if (checkedPermissions.IsFailure)
        {
            return checkedPermissions.Error;
        }

        return new Role(Guid.CreateVersion7(), checkedName.Value, checkedPermissions.Value);
    }

    public Result Rename(string? name)
    {
        var checkedName = CheckName(name);
        if (checkedName.IsFailure)
        {
            return checkedName.Error;
        }

        Name = checkedName.Value;
        NormalizedName = Normalize(Name);
        return Result.Success();
    }

    /// <summary>Replaces the role's permissions; holders get them in their next access token.</summary>
    public Result SetPermissions(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var checkedPermissions = CheckPermissions(permissions);
        if (checkedPermissions.IsFailure)
        {
            return checkedPermissions.Error;
        }

        _permissions = checkedPermissions.Value;
        return Result.Success();
    }

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static Result<string> CheckName(string? name)
    {
        var trimmed = name?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return RoleErrors.NameRequired;
        }

        return trimmed.Length > NameMaxLength ? RoleErrors.NameTooLong : trimmed;
    }

    // Duplicates collapse; the order is fixed so that saving the same set changes nothing.
    private static Result<List<string>> CheckPermissions(IEnumerable<string> permissions)
    {
        var distinct = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var unknown = distinct.FirstOrDefault(permission => !Roles.Permissions.IsKnown(permission));
        if (unknown is not null)
        {
            return RoleErrors.UnknownPermission(unknown);
        }

        return distinct;
    }
}
