using Shared.Domain;

namespace Auth.Domain.Roles;

/// <summary>
/// A named set of permissions. Users get roles; code checks permissions, never role names.
/// </summary>
public sealed class Role : AggregateRoot, IAuditableEntity
{
    public const int NameMaxLength = 64;

    /// <summary>The name of the system role that the seed creates; see <see cref="IsSystem"/>.</summary>
    public const string AdministratorName = "Administrator";

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

    /// <summary>
    /// The administrator role: it always holds every permission in the catalog and cannot be
    /// renamed, changed or deleted through the API. Only the seed creates it and keeps it in step
    /// with the catalog, so administrators cannot lock themselves out by editing roles.
    /// </summary>
    public bool IsSystem { get; private set; }

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

    /// <summary>The <see cref="IsSystem"/> administrator role, holding every permission in the catalog.</summary>
    public static Role CreateAdministrator()
        => new(Guid.CreateVersion7(), AdministratorName, [.. Roles.Permissions.All.Order(StringComparer.Ordinal)]) { IsSystem = true };

    /// <summary>
    /// Gives the system role every permission in the catalog, including ones added since the last
    /// run. Returns whether anything changed.
    /// </summary>
    public bool SyncSystemPermissions()
    {
        if (!IsSystem)
        {
            throw new InvalidOperationException("Only the system role follows the permission catalog.");
        }

        List<string> all = [.. Roles.Permissions.All.Order(StringComparer.Ordinal)];
        if (_permissions.SequenceEqual(all, StringComparer.Ordinal))
        {
            return false;
        }

        _permissions = all;
        return true;
    }

    public Result Rename(string? name)
    {
        if (IsSystem)
        {
            return RoleErrors.SystemRole;
        }

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

        if (IsSystem)
        {
            return RoleErrors.SystemRole;
        }

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
