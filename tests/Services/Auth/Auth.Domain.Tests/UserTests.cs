using Auth.Domain.Users;

namespace Auth.Domain.Tests;

public sealed class EmailAddressTests
{
    [Fact]
    public void Address_IsTrimmedAndLowerCased()
    {
        Assert.Equal("jane.doe@example.com", EmailAddress.Create("  Jane.Doe@Example.COM ").Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankAddress_IsRejected(string? value)
    {
        Assert.Equal(UserErrors.EmailRequired, EmailAddress.Create(value).Error);
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@example.com")]
    [InlineData("jane@")]
    [InlineData("Jane Doe <jane@example.com>")]
    [InlineData("jane doe@example.com")]
    public void MalformedAddress_IsRejected(string value)
    {
        Assert.Equal(UserErrors.EmailInvalid, EmailAddress.Create(value).Error);
    }

    [Fact]
    public void TooLongAddress_IsRejected()
    {
        var value = new string('a', EmailAddress.MaxLength - "@example.com".Length + 1) + "@example.com";

        Assert.Equal(UserErrors.EmailTooLong, EmailAddress.Create(value).Error);
    }
}

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingPassword_IsRejected(string? password)
    {
        Assert.Equal(UserErrors.PasswordRequired, PasswordPolicy.Check(password).Error);
    }

    [Fact]
    public void ShortPassword_IsRejected()
    {
        Assert.Equal(UserErrors.PasswordTooShort, PasswordPolicy.Check(new string('a', PasswordPolicy.MinLength - 1)).Error);
    }

    [Fact]
    public void LongPassword_IsRejected()
    {
        Assert.Equal(UserErrors.PasswordTooLong, PasswordPolicy.Check(new string('a', PasswordPolicy.MaxLength + 1)).Error);
    }

    [Theory]
    [InlineData("correct horse battery staple")]
    [InlineData("alllowercaseletters")]
    public void LongEnoughPassword_IsAcceptedWithoutCompositionRules(string password)
    {
        Assert.True(PasswordPolicy.Check(password).IsSuccess);
    }
}

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Register_RaisesUserRegistered()
    {
        var user = NewUser();

        var raised = Assert.IsType<UserRegisteredDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.Id, raised.UserId);
        Assert.Equal("jane@example.com", raised.Email);
        Assert.Equal(7, user.Id.Version);
    }

    [Fact]
    public void NewUser_CanSignIn()
    {
        Assert.True(NewUser().CanSignIn(Now).IsSuccess);
    }

    [Fact]
    public void DisabledUser_CannotSignIn()
    {
        var user = NewUser();
        user.Disable();

        Assert.Equal(UserErrors.Disabled, user.CanSignIn(Now).Error);
    }

    [Fact]
    public void Enable_LiftsTheDisableAndAnyLockout()
    {
        var user = NewUser();
        user.Disable();

        user.Enable();

        Assert.True(user.CanSignIn(Now).IsSuccess);
        Assert.Equal(0, user.FailedSignInCount);
    }

    [Fact]
    public void AssignRole_IsIdempotent()
    {
        var user = NewUser();
        var roleId = Guid.CreateVersion7();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        var role = Assert.Single(user.Roles);
        Assert.Equal(roleId, role.RoleId);
        Assert.Equal(user.Id, role.UserId);
    }

    [Fact]
    public void RemoveRole_TakesTheRoleAway()
    {
        var user = NewUser();
        var roleId = Guid.CreateVersion7();
        user.AssignRole(roleId);

        user.RemoveRole(roleId);

        Assert.Empty(user.Roles);
    }

    [Fact]
    public void ChangePasswordHash_ReplacesTheHash()
    {
        var user = NewUser();

        user.ChangePasswordHash("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }

    private static User NewUser() => User.Register(EmailAddress.Create("jane@example.com").Value, "hash");
}
