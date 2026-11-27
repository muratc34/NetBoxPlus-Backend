using Notification.Domain.Emails;
using Notification.Domain.SignIns;

namespace Notification.Domain.Tests;

public sealed class SignInRecordTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ADevice_IsKnownForThirtyDays_AndALateOldSignInDoesNotMoveItBack()
    {
        var device = KnownDevice.Create(Guid.CreateVersion7(), "Chrome · Windows", Now);

        device.Seen(Now.AddDays(-3));

        Assert.True(device.IsKnownAt(Now + KnownDevice.RememberedFor - TimeSpan.FromSeconds(1)));
        Assert.False(device.IsKnownAt(Now + KnownDevice.RememberedFor));
        Assert.Equal(Now, device.LastSeenAt);
    }

    [Fact]
    public void ALabel_IsTrimmed_CutToItsLength_AndEmptyWhenUnknown()
    {
        Assert.Equal("Chrome · Windows", KnownDevice.LabelOf("  Chrome · Windows "));
        Assert.Equal(string.Empty, KnownDevice.LabelOf(null));
        Assert.Equal(KnownDevice.LabelMaxLength, KnownDevice.LabelOf(new string('x', 300)).Length);
    }

    [Fact]
    public void ALink_KeepsOnlyTheHashOfItsToken_AndExpires()
    {
        var (link, token) = UnsubscribeLink.Issue(Guid.CreateVersion7(), Now);

        Assert.Equal(UnsubscribeLink.Hash(token), link.TokenHash);
        Assert.NotEqual(token, Convert.ToBase64String(link.TokenHash));
        Assert.True(link.IsValidAt(Now + UnsubscribeLink.Lifetime - TimeSpan.FromSeconds(1)));
        Assert.False(link.IsValidAt(Now + UnsubscribeLink.Lifetime));
    }

    [Fact]
    public void ThePreference_StartsOn_AndRemembersWhenItChanged()
    {
        var preference = NotificationPreference.Create(Guid.CreateVersion7());
        Assert.True(preference.SignInAlerts);

        preference.Change(true, Now);
        Assert.Null(preference.ChangedAt);

        preference.Change(false, Now);
        Assert.Equal((false, (DateTimeOffset?)Now), (preference.SignInAlerts, preference.ChangedAt));
    }

    [Fact]
    public void TheUnsubscribeAddress_IsForgottenWithTheContent_AndMustBeAbsolute()
    {
        var content = new EmailContent("Subject", "<p>Hi</p>", "Hi");
        var email = OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.NewSignIn, "key", content, Now, unsubscribeUrl: new Uri("https://api.example.test/unsubscribe?token=t")).Value;

        email.MarkSent(Now);

        Assert.Null(email.UnsubscribeUrl);
        Assert.Equal(
            OutgoingEmailErrors.UnsubscribeUrlInvalid,
            OutgoingEmail.Queue(Guid.CreateVersion7(), EmailKind.NewSignIn, "key", content, Now, unsubscribeUrl: new Uri("unsubscribe", UriKind.Relative)).Error);
    }
}
