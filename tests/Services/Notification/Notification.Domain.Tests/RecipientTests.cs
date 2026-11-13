using Notification.Domain.Recipients;

namespace Notification.Domain.Tests;

public sealed class RecipientTests
{
    [Fact]
    public void ARecipient_KeepsTheUsersAddress_Trimmed()
    {
        var user = Guid.CreateVersion7();

        var recipient = Recipient.Create(user, "  viewer@example.com ").Value;

        Assert.Equal((user, "viewer@example.com"), (recipient.Id, recipient.Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public void AnAddressThatIsNone_IsRefused(string? email)
        => Assert.Equal(RecipientErrors.EmailInvalid, Recipient.Create(Guid.CreateVersion7(), email).Error);

    [Fact]
    public void ATooLongAddress_IsRefused()
        => Assert.Equal(RecipientErrors.EmailInvalid, Recipient.Create(Guid.CreateVersion7(), new string('a', Recipient.EmailMaxLength - 11) + "@example.com").Error);

    [Fact]
    public void TheLongestAddress_IsKept()
        => Assert.True(Recipient.Create(Guid.CreateVersion7(), new string('a', Recipient.EmailMaxLength - 12) + "@example.com").IsSuccess);

    [Fact]
    public void ARecipient_NeedsAUser()
        => Assert.Equal(RecipientErrors.UserRequired, Recipient.Create(Guid.Empty, "viewer@example.com").Error);
}
