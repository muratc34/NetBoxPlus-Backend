using Auth.Domain.Passkeys;

namespace Auth.Domain.Tests;

public sealed class PasskeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    [Fact]
    public void Register_KeepsTheTrimmedName_AndAnnouncesIt()
    {
        var passkey = UserPasskey.Register(UserId, [1, 2], [3, 4], 0, "  iPhone  ", Now).Value;

        Assert.Equal("iPhone", passkey.Name);
        var added = Assert.IsType<PasskeyAddedDomainEvent>(Assert.Single(passkey.DomainEvents));
        Assert.Equal((passkey.Id, UserId, "iPhone"), (added.PasskeyId, added.UserId, added.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ANameless_OrTooLongName_IsRefused(string? name)
    {
        Assert.Equal(PasskeyErrors.NameInvalid, UserPasskey.Register(UserId, [1], [2], 0, name!, Now).Error);
        Assert.Equal(PasskeyErrors.NameInvalid, UserPasskey.Register(UserId, [1], [2], 0, new string('a', UserPasskey.MaxNameLength + 1), Now).Error);
    }

    [Fact]
    public void AnEmptyOrOversizedCredential_IsRefused()
    {
        Assert.Equal(PasskeyErrors.Invalid, UserPasskey.Register(UserId, [], [2], 0, "Key", Now).Error);
        Assert.Equal(PasskeyErrors.Invalid, UserPasskey.Register(UserId, new byte[UserPasskey.MaxCredentialIdLength + 1], [2], 0, "Key", Now).Error);
        Assert.Equal(PasskeyErrors.Invalid, UserPasskey.Register(UserId, [1], [], 0, "Key", Now).Error);
    }

    [Fact]
    public void Using_RecordsTheCounterAndTime_AndRemovingAnnouncesIt()
    {
        var passkey = UserPasskey.Register(UserId, [1], [2], 3, "Key", Now).Value;
        passkey.ClearDomainEvents();

        passkey.RecordUse(4, Now.AddDays(1));
        passkey.Remove(Now.AddDays(2));

        Assert.Equal((4L, (DateTimeOffset?)Now.AddDays(1)), (passkey.SignCount, passkey.LastUsedAt));
        Assert.IsType<PasskeyRemovedDomainEvent>(Assert.Single(passkey.DomainEvents));
    }

    [Fact]
    public void AChallenge_LivesFiveMinutes_AndNamesItsCeremony()
    {
        var registration = PasskeyChallenge.ForRegistration(UserId, "{}", Now);
        var signIn = PasskeyChallenge.ForSignIn("{}", Now);

        Assert.Equal((PasskeyCeremony.Registration, (Guid?)UserId, Now.AddMinutes(5)), (registration.Ceremony, registration.UserId, registration.ExpiresAt));
        Assert.Equal((PasskeyCeremony.SignIn, (Guid?)null), (signIn.Ceremony, signIn.UserId));
    }
}
