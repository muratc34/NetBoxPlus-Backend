using Auth.Domain.Users;

namespace Auth.Domain.Tests;

public sealed class ProfilePinTests
{
    private static User NewUser() => User.Register(EmailAddress.Create("viewer@example.com").Value, "hash");

    [Theory]
    [InlineData("0000", true)]
    [InlineData("9876", true)]
    [InlineData("123", false)]
    [InlineData("12345", false)]
    [InlineData("12a4", false)]
    [InlineData("١٢٣٤", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void APin_IsExactlyFourAsciiDigits(string? pin, bool wellFormed) => Assert.Equal(wellFormed, Profile.IsWellFormedPin(pin));

    [Fact]
    public void PuttingAPin_LocksTheProfile_AndEveryChangeIsAnnounced()
    {
        var user = NewUser();
        var profile = user.Profiles.Single();
        user.ClearDomainEvents();

        Assert.True(user.ChangeProfilePin(profile.Id, "pin-hash").IsSuccess);
        Assert.True(user.ChangeProfilePin(profile.Id, "another-hash").IsSuccess);
        Assert.True(user.ChangeProfilePin(profile.Id, null).IsSuccess);

        Assert.Equal(
            [(2, true), (3, true), (4, false)],
            user.DomainEvents.Cast<ProfileChangedDomainEvent>().Select(changed => (changed.Revision, changed.Locked)));
        Assert.False(profile.IsLocked);
    }

    [Fact]
    public void TakingOffAMissingPin_ChangesNothing_AndAnUnknownProfileIsNotFound()
    {
        var user = NewUser();
        var profile = user.Profiles.Single();
        user.ClearDomainEvents();

        Assert.True(user.ChangeProfilePin(profile.Id, null).IsSuccess);
        Assert.Empty(user.DomainEvents);
        Assert.Equal(1, profile.Revision);
        Assert.Equal(ProfileErrors.NotFound, user.ChangeProfilePin(Guid.NewGuid(), "pin-hash").Error);
    }

    [Fact]
    public void ARenamedLockedProfile_StaysLockedInItsEvent()
    {
        var user = NewUser();
        var profile = user.Profiles.Single();
        user.ChangeProfilePin(profile.Id, "pin-hash");
        user.ClearDomainEvents();

        user.ChangeProfile(profile.Id, "Parents", AgeRating.Age18);

        Assert.True(Assert.IsType<ProfileChangedDomainEvent>(Assert.Single(user.DomainEvents)).Locked);
    }

}
