using Auth.Domain.Roles;

namespace Auth.Domain.Tests;

public sealed class PermissionsTests
{
    [Fact]
    public void Catalog_CollectsEveryDeclaredConstant()
    {
        Assert.Contains(Permissions.Auth.UsersRead, Permissions.All);
        Assert.Contains(Permissions.Sample.NotesDelete, Permissions.All);
    }

    [Fact]
    public void EveryPermission_IsServiceResourceAction()
    {
        Assert.All(Permissions.All, permission => Assert.True(
            Permissions.IsWellFormed(permission),
            $"'{permission}' is not <service>:<resource>:<action> in lower case."));
    }

    [Fact]
    public void EveryPermission_StartsWithTheServiceItIsDeclaredUnder()
    {
        foreach (var service in typeof(Permissions).GetNestedTypes())
        {
            var prefix = service.Name.ToLowerInvariant() + ":";
            Assert.All(
                service.GetFields().Select(field => (string)field.GetRawConstantValue()!),
                permission => Assert.StartsWith(prefix, permission, StringComparison.Ordinal));
        }
    }
}

public sealed class RoleTests
{
    [Fact]
    public void Create_KeepsTheNameAndSortsDistinctPermissions()
    {
        var role = Role.Create(
            "  Content editor ",
            [Permissions.Auth.UsersRead, Permissions.Sample.NotesDelete, Permissions.Auth.UsersRead]).Value;

        Assert.Equal("Content editor", role.Name);
        Assert.Equal("CONTENT EDITOR", role.NormalizedName);
        Assert.Equal([Permissions.Auth.UsersRead, Permissions.Sample.NotesDelete], role.Permissions);
    }

    [Fact]
    public void UnknownPermission_IsRejected()
    {
        var result = Role.Create("Editor", ["catalog:movies:fly"]);

        Assert.Equal("Role.UnknownPermission", result.Error.Code);
        Assert.Contains("catalog:movies:fly", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void BlankName_IsRejected(string? name)
    {
        Assert.Equal(RoleErrors.NameRequired, Role.Create(name, []).Error);
    }

    [Fact]
    public void TooLongName_IsRejected()
    {
        Assert.Equal(RoleErrors.NameTooLong, Role.Create(new string('a', Role.NameMaxLength + 1), []).Error);
    }

    [Fact]
    public void SetPermissions_ReplacesTheSet()
    {
        var role = Role.Create("Editor", [Permissions.Auth.UsersRead]).Value;

        Assert.True(role.SetPermissions([Permissions.Auth.RolesRead]).IsSuccess);

        Assert.Equal([Permissions.Auth.RolesRead], role.Permissions);
    }

    [Fact]
    public void SetPermissions_WithAnUnknownPermission_ChangesNothing()
    {
        var role = Role.Create("Editor", [Permissions.Auth.UsersRead]).Value;

        Assert.True(role.SetPermissions(["auth:users:fly"]).IsFailure);

        Assert.Equal([Permissions.Auth.UsersRead], role.Permissions);
    }

    [Fact]
    public void Rename_UpdatesTheNormalizedName()
    {
        var role = Role.Create("Editor", []).Value;

        Assert.True(role.Rename("Moderator").IsSuccess);

        Assert.Equal("MODERATOR", role.NormalizedName);
    }

    [Fact]
    public void CreateAdministrator_IsTheSystemRoleWithEveryPermission()
    {
        var role = Role.CreateAdministrator();

        Assert.True(role.IsSystem);
        Assert.Equal(Role.AdministratorName, role.Name);
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), role.Permissions);
    }

    [Fact]
    public void SystemRole_CannotBeRenamedOrChanged()
    {
        var role = Role.CreateAdministrator();

        Assert.Equal(RoleErrors.SystemRole, role.Rename("Owner").Error);
        Assert.Equal(RoleErrors.SystemRole, role.SetPermissions([Permissions.Auth.UsersRead]).Error);
        Assert.Equal(Role.AdministratorName, role.Name);
        Assert.Equal(Permissions.All.Count, role.Permissions.Count);
    }

    [Fact]
    public void SyncSystemPermissions_WhenInStep_ChangesNothing()
    {
        Assert.False(Role.CreateAdministrator().SyncSystemPermissions());
    }

    [Fact]
    public void SyncSystemPermissions_OnAnOrdinaryRole_Throws()
    {
        var role = Role.Create("Editor", []).Value;

        Assert.Throws<InvalidOperationException>(() => role.SyncSystemPermissions());
    }
}
