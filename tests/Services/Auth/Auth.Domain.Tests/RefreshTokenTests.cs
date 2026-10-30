using Auth.Domain.RefreshTokens;

namespace Auth.Domain.Tests;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void Issue_StartsAFamilyWithTheCustomerLifetimes()
    {
        var token = RefreshToken.Issue(UserId, SessionKind.Customer, Hash(1), Now);

        Assert.Equal(UserId, token.UserId);
        Assert.NotEqual(Guid.Empty, token.FamilyId);
        Assert.Equal(Now + TimeSpan.FromDays(7), token.ExpiresAt);
        Assert.Equal(Now + TimeSpan.FromDays(30), token.FamilyExpiresAt);
        Assert.True(token.IsActive(Now));
        Assert.False(token.IsSpent);
    }

    [Fact]
    public void AdminFamily_LivesOneWorkingDay()
    {
        var token = RefreshToken.Issue(UserId, SessionKind.Admin, Hash(1), Now);

        Assert.Equal(Now + TimeSpan.FromHours(8), token.ExpiresAt);
        Assert.Equal(Now + TimeSpan.FromHours(8), token.FamilyExpiresAt);
    }

    [Fact]
    public void Rotate_StaysInTheFamilyAndExtendsTheToken()
    {
        var first = RefreshToken.Issue(UserId, SessionKind.Customer, Hash(1), Now);
        var later = Now + TimeSpan.FromDays(5);

        var next = first.Rotate(Hash(2), later);

        Assert.Equal(first.FamilyId, next.FamilyId);
        Assert.Equal(first.UserId, next.UserId);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(later + TimeSpan.FromDays(7), next.ExpiresAt);
        Assert.Equal(first.FamilyExpiresAt, next.FamilyExpiresAt);
    }

    [Fact]
    public void Rotate_NeverOutlivesTheFamily()
    {
        var first = RefreshToken.Issue(UserId, SessionKind.Customer, Hash(1), Now);
        var nearTheEnd = Now + TimeSpan.FromDays(28);

        var next = first.Rotate(Hash(2), nearTheEnd);

        Assert.Equal(first.FamilyExpiresAt, next.ExpiresAt);
    }

    [Fact]
    public void ExpiredToken_IsNotActive()
    {
        var token = RefreshToken.Issue(UserId, SessionKind.Customer, Hash(1), Now);

        Assert.False(token.IsActive(token.ExpiresAt));
    }

    [Fact]
    public void HashOfTheWrongLength_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RefreshToken.Issue(UserId, SessionKind.Customer, new byte[16], Now));
    }

    [Fact]
    public void UnknownKind_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RefreshTokenLifetime.For((SessionKind)99));
    }

    private static byte[] Hash(byte seed) => Enumerable.Repeat(seed, RefreshToken.HashLength).ToArray();
}
