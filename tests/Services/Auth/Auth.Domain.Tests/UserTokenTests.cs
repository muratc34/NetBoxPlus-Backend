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
        Assert.DoesNotContain(typeof(UserToken).GetProperties(), property => property.PropertyType == typeof(string));
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
