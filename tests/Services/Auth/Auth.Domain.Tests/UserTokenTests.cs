using Auth.Domain.Users;
using Auth.Domain.UserTokens;

namespace Auth.Domain.Tests;

public sealed class UserTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void IssueEmailVerification_LivesADay_AndHandsTheValueOnlyToItsEvent()
    {
        var token = UserToken.IssueEmailVerification(UserId, "raw-value", Hash(1), Now);

        Assert.Equal((UserId, UserTokenPurpose.EmailVerification, Now, Now + TimeSpan.FromHours(24)), (token.UserId, token.Purpose, token.CreatedAt, token.ExpiresAt));
        Assert.Equal(Hash(1), token.TokenHash);
        Assert.True(token.IsActive(Now));
        var issued = Assert.IsType<EmailVerificationTokenIssuedDomainEvent>(Assert.Single(token.DomainEvents));
        Assert.Equal(new EmailVerificationTokenIssuedDomainEvent(UserId, token.Id, "raw-value", token.ExpiresAt), issued);
        // The value is never kept: the only text a token stores is an e-mail change's new address.
        Assert.Equal(
            [nameof(UserToken.Email)],
            typeof(UserToken).GetProperties().Where(property => property.PropertyType == typeof(string)).Select(property => property.Name));
        Assert.Null(token.Email);
    }

    [Fact]
    public void IssuePasswordReset_LivesAnHour_AndHandsTheValueOnlyToItsEvent()
    {
        var token = UserToken.IssuePasswordReset(UserId, "raw-value", Hash(2), Now);

        Assert.Equal((UserId, UserTokenPurpose.PasswordReset, Now, Now + TimeSpan.FromHours(1)), (token.UserId, token.Purpose, token.CreatedAt, token.ExpiresAt));
        Assert.Equal(Hash(2), token.TokenHash);
        Assert.True(token.IsActive(token.ExpiresAt.AddTicks(-1)));
        Assert.False(token.IsActive(token.ExpiresAt));
        var issued = Assert.IsType<PasswordResetTokenIssuedDomainEvent>(Assert.Single(token.DomainEvents));
        Assert.Equal(new PasswordResetTokenIssuedDomainEvent(UserId, token.Id, "raw-value", token.ExpiresAt), issued);
    }

    [Fact]
    public void IssueEmailChange_KeepsTheNewAddress_LivesADay_AndHandsTheValueOnlyToItsEvent()
    {
        var token = UserToken.IssueEmailChange(UserId, "new@example.com", "raw-value", Hash(3), Now);

        Assert.Equal((UserTokenPurpose.EmailChange, "new@example.com", Now + TimeSpan.FromHours(24)), (token.Purpose, token.Email, token.ExpiresAt));
        Assert.Equal(Hash(3), token.TokenHash);
        var issued = Assert.IsType<EmailChangeTokenIssuedDomainEvent>(Assert.Single(token.DomainEvents));
        Assert.Equal(new EmailChangeTokenIssuedDomainEvent(UserId, token.Id, "raw-value", "new@example.com", token.ExpiresAt), issued);
        Assert.Null(UserToken.IssuePasswordReset(UserId, "raw-value", Hash(2), Now).Email);
    }

    [Fact]
    public void AnEmailChangeTokenWithoutValidInput_IsAProgrammingError()
    {
        Assert.ThrowsAny<ArgumentException>(() => UserToken.IssueEmailChange(UserId, " ", "raw-value", Hash(1), Now));
        Assert.ThrowsAny<ArgumentException>(() => UserToken.IssueEmailChange(UserId, "new@example.com", " ", Hash(1), Now));
        Assert.Throws<ArgumentException>(() => UserToken.IssueEmailChange(UserId, "new@example.com", "raw-value", new byte[31], Now));
    }

    [Fact]
    public void APasswordResetTokenWithoutValidInput_IsAProgrammingError()
    {
        Assert.ThrowsAny<ArgumentException>(() => UserToken.IssuePasswordReset(UserId, " ", Hash(1), Now));
        Assert.ThrowsAny<ArgumentException>(() => UserToken.IssuePasswordReset(Guid.Empty, "raw-value", Hash(1), Now));
        Assert.Throws<ArgumentException>(() => UserToken.IssuePasswordReset(UserId, "raw-value", new byte[31], Now));
    }

    [Fact]
    public void ATokenAtItsExpiry_IsNoLongerActive()
    {
        var token = UserToken.IssueEmailVerification(UserId, "raw-value", Hash(1), Now);

        Assert.True(token.IsActive(token.ExpiresAt.AddTicks(-1)));
        Assert.False(token.IsActive(token.ExpiresAt));
    }

    [Fact]
    public void Revoke_EndsAnUnusedToken_AndKeepsTheFirstTime()
    {
        var token = UserToken.IssueEmailVerification(UserId, "raw-value", Hash(1), Now);

        token.Revoke(Now.AddMinutes(1));
        token.Revoke(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(1), token.RevokedAt);
        Assert.False(token.IsActive(Now.AddMinutes(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(33)]
    public void AHashOfTheWrongLength_IsAProgrammingError(int length)
        => Assert.Throws<ArgumentException>(() => UserToken.IssueEmailVerification(UserId, "raw-value", new byte[length], Now));

    [Fact]
    public void AnEmptyValueOrUser_IsAProgrammingError()
    {
        Assert.ThrowsAny<ArgumentException>(() => UserToken.IssueEmailVerification(UserId, " ", Hash(1), Now));
        Assert.ThrowsAny<ArgumentException>(() => UserToken.IssueEmailVerification(Guid.Empty, "raw-value", Hash(1), Now));
    }

    [Fact]
    public void VerifyEmail_KeepsTheFirstTime()
    {
        var user = User.Register(EmailAddress.Create("viewer@example.com").Value, "hash");
        Assert.False(user.IsEmailVerified);

        user.VerifyEmail(Now);
        user.VerifyEmail(Now.AddDays(1));

        Assert.True(user.IsEmailVerified);
        Assert.Equal(Now, user.EmailVerifiedAt);
    }

    private static byte[] Hash(byte fill) => Enumerable.Repeat(fill, UserToken.HashLength).ToArray();
}
