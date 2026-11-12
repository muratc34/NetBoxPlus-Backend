using Auth.Domain.Users;

namespace Auth.Domain.Tests;

public sealed class ProfileTests
{
    private static User NewUser() => User.Register(EmailAddress.Create("viewer@example.com").Value, "hash");

    [Fact]
    public void Registering_GivesTheAccountItsFirstProfile()
    {
        var user = NewUser();

        var profile = Assert.Single(user.Profiles);
        Assert.Equal(("Main", AgeRating.Age18, 1), (profile.Name, profile.MaxAgeRating, profile.Revision));
        Assert.Contains(user.DomainEvents, item => item is ProfileCreatedDomainEvent created && created.ProfileId == profile.Id);
    }

    [Fact]
    public void AddProfile_TrimsTheName_AndRaisesAnEvent()
    {
        var user = NewUser();
        user.ClearDomainEvents();

        var kids = user.AddProfile("  Kids  ", AgeRating.Age7).Value;

        Assert.Equal(("Kids", AgeRating.Age7), (kids.Name, kids.MaxAgeRating));
        var created = Assert.IsType<ProfileCreatedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal((user.Id, kids.Id, AgeRating.Age7, 1), (created.UserId, created.ProfileId, created.MaxAgeRating, created.Revision));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void AProfile_NeedsAName(string? name) => Assert.Equal(ProfileErrors.NameRequired, NewUser().AddProfile(name, AgeRating.All).Error);

    [Fact]
    public void ANameMayNotBeTooLong() => Assert.Equal(ProfileErrors.NameTooLong, NewUser().AddProfile(new string('a', Profile.NameMaxLength + 1), AgeRating.All).Error);

    [Fact]
    public void AnUnknownAgeRating_IsRefused() => Assert.Equal(ProfileErrors.AgeRatingInvalid, NewUser().AddProfile("Kids", (AgeRating)42).Error);

    [Fact]
    public void Names_AreUniqueInAnAccount_WhateverTheCase()
    {
        var user = NewUser();

        Assert.Equal(ProfileErrors.NameTaken, user.AddProfile("main", AgeRating.All).Error);
    }

    [Fact]
    public void AnAccount_HasAtMostFiveProfiles()
    {
        var user = NewUser();
        for (var i = 2; i <= User.MaxProfiles; i++)
        {
            Assert.True(user.AddProfile($"Profile {i}", AgeRating.All).IsSuccess);
        }

        Assert.Equal(ProfileErrors.LimitReached, user.AddProfile("One more", AgeRating.All).Error);
        Assert.Equal(User.MaxProfiles, user.Profiles.Count);
    }

    [Fact]
    public void Change_RaisesTheRevision_OnlyWhenSomethingChanged()
    {
        var user = NewUser();
        var main = user.Profiles.Single();
        user.ClearDomainEvents();

        Assert.True(user.ChangeProfile(main.Id, "Main", AgeRating.Age18).IsSuccess);
        Assert.Empty(user.DomainEvents);

        Assert.True(user.ChangeProfile(main.Id, "Family", AgeRating.Age13).IsSuccess);
        var changed = Assert.IsType<ProfileChangedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal((AgeRating.Age13, 2), (changed.MaxAgeRating, changed.Revision));
        Assert.Equal("Family", main.Name);
    }

    [Fact]
    public void Change_KeepsNamesUnique_ButAProfileMayKeepItsOwnName()
    {
        var user = NewUser();
        var kids = user.AddProfile("Kids", AgeRating.Age7).Value;

        Assert.Equal(ProfileErrors.NameTaken, user.ChangeProfile(kids.Id, "MAIN", AgeRating.Age7).Error);
        Assert.True(user.ChangeProfile(kids.Id, "kids", AgeRating.Age7).IsSuccess);
        Assert.Equal(ProfileErrors.NotFound, user.ChangeProfile(Guid.NewGuid(), "x", AgeRating.All).Error);
    }

    [Fact]
    public void Remove_TakesTheProfileAway_ButNeverTheLast()
    {
        var user = NewUser();
        var main = user.Profiles.Single();
        var kids = user.AddProfile("Kids", AgeRating.Age7).Value;
        user.ClearDomainEvents();

        Assert.True(user.RemoveProfile(kids.Id).IsSuccess);
        var removed = Assert.IsType<ProfileRemovedDomainEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal((kids.Id, 2), (removed.ProfileId, removed.Revision));

        Assert.Equal(ProfileErrors.LastProfile, user.RemoveProfile(main.Id).Error);
        Assert.Equal(ProfileErrors.NotFound, user.RemoveProfile(kids.Id).Error);
    }
}
