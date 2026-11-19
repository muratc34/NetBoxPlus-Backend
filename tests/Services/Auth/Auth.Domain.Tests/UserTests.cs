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

        // The first profile is announced alongside (see ProfileTests).
        var raised = Assert.Single(user.DomainEvents.OfType<UserRegisteredDomainEvent>());
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
    public void RehashPassword_ReplacesTheHash_WithoutTellingTheUser()
    {
        var user = NewUser();
        user.ClearDomainEvents();

        user.RehashPassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangePassword_ReplacesTheHash_AndAnnouncesTheChange()
    {
        var user = NewUser();
        user.ClearDomainEvents();
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        user.ChangePassword("new-hash", now);

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.False(user.IsEmailVerified);
        var changed = Assert.IsType<PasswordChangedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal((user.Id, now), (changed.UserId, changed.ChangedAt));
        Assert.NotEqual(Guid.Empty, changed.ChangeId);
    }

    [Fact]
    public void EachPasswordChange_HasItsOwnId()
    {
        var user = NewUser();
        user.ClearDomainEvents();
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        user.ChangePassword("hash-1", now);
        user.ChangePassword("hash-2", now);

        var ids = user.DomainEvents.OfType<PasswordChangedDomainEvent>().Select(changed => changed.ChangeId).ToList();
        Assert.Equal(2, ids.Distinct().Count());
    }

    [Fact]
    public void ResetPassword_ChangesThePassword_AndVerifiesTheAddress()
    {
        var user = NewUser();
        user.ClearDomainEvents();
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        user.ResetPassword("new-hash", now);

        Assert.Equal(("new-hash", now), (user.PasswordHash, user.EmailVerifiedAt));
        Assert.Collection(
            user.DomainEvents,
            changed => Assert.IsType<PasswordChangedDomainEvent>(changed),
            verified => Assert.Equal(new EmailVerifiedDomainEvent(user.Id, user.Email.Value, now), verified));

        // Already verified: the password changes, the verification is not announced again.
        user.ClearDomainEvents();
        user.ResetPassword("newer-hash", now.AddDays(1));
        Assert.IsType<PasswordChangedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal(now, user.EmailVerifiedAt);
    }

    [Fact]
    public void VerifyingTheAddress_IsAnnouncedOnce_AndKeepsTheFirstTime()
    {
        var user = NewUser();
        user.ClearDomainEvents();
        var first = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

        user.VerifyEmail(first);
        user.VerifyEmail(first.AddHours(1));

        Assert.Equal(new EmailVerifiedDomainEvent(user.Id, user.Email.Value, first), Assert.Single(user.DomainEvents));
        Assert.Equal(first, user.EmailVerifiedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AnEmptyHash_IsAProgrammingError(string hash)
    {
        var user = NewUser();
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.ThrowsAny<ArgumentException>(() => user.RehashPassword(hash));
        Assert.ThrowsAny<ArgumentException>(() => user.ChangePassword(hash, now));
        Assert.ThrowsAny<ArgumentException>(() => user.ResetPassword(hash, now));
    }

    [Fact]
    public void ChangeEmail_MovesTheAccount_RaisesTheNextRevision_AndVerifiesTheFirstTime()
    {
        var user = NewUser();
        user.ClearDomainEvents();
        var newEmail = EmailAddress.Create("jane.new@example.com").Value;

        user.ChangeEmail(newEmail, Now);

        Assert.Equal((newEmail, 2, Now), (user.Email, user.EmailRevision, user.EmailVerifiedAt));
        Assert.Equal(
            [new EmailChangedDomainEvent(user.Id, "jane.new@example.com", 2, Now), new EmailVerifiedDomainEvent(user.Id, "jane.new@example.com", Now)],
            user.DomainEvents);
    }

    [Fact]
    public void ChangeEmail_OfAVerifiedAccount_KeepsItVerified_WithoutAnnouncingIt()
    {
        var user = NewUser();
        user.VerifyEmail(Now);
        user.ClearDomainEvents();
        var later = Now.AddDays(3);

        user.ChangeEmail(EmailAddress.Create("jane.new@example.com").Value, later);
        user.ChangeEmail(EmailAddress.Create("jane.third@example.com").Value, later.AddDays(1));

        Assert.Equal((3, later.AddDays(1)), (user.EmailRevision, user.EmailVerifiedAt));
        Assert.Equal([2, 3], user.DomainEvents.OfType<EmailChangedDomainEvent>().Select(changed => changed.Revision));
        Assert.Empty(user.DomainEvents.OfType<EmailVerifiedDomainEvent>());
    }

    [Fact]
    public void ChangeEmail_ToTheSameAddress_ChangesNothing()
    {
        var user = NewUser();
        user.ClearDomainEvents();

        user.ChangeEmail(EmailAddress.Create("JANE@example.com").Value, Now);

        Assert.Equal((1, (DateTimeOffset?)null), (user.EmailRevision, user.EmailVerifiedAt));
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void Delete_AnonymizesTheAccount_AndIsAnnouncedOnce()
    {
        var user = NewUser();
        user.AssignRole(Guid.CreateVersion7());
        user.VerifyEmail(Now);
        user.ClearDomainEvents();

        user.Delete(Now);
        user.Delete(Now.AddDays(1));

        Assert.Equal($"deleted-{user.Id:N}@deleted.invalid", user.Email.Value);
        Assert.Equal((User.DeletedPasswordHash, true, (DateTimeOffset?)Now, 2), (user.PasswordHash, user.IsDisabled, user.DeletedAt, user.EmailRevision));
        Assert.Null(user.EmailVerifiedAt);
        Assert.Empty(user.Roles);
        Assert.Empty(user.Profiles);
        Assert.Equal([new UserDeletedDomainEvent(user.Id, Now)], user.DomainEvents);
    }

    private static User NewUser() => User.Register(EmailAddress.Create("jane@example.com").Value, "hash");
}
