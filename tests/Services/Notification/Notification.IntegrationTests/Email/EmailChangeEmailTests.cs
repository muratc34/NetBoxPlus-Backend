using System.Globalization;
using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Domain.Emails;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.IntegrationTests.Email;

/// <summary>An address change as the messaging layer delivers it: the link to the new address, the notice to the old one.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class EmailChangeEmailTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string LinkSubject = "Yeni e-posta adresini onayla";
    private const string NoticeSubject = "E-posta adresin değiştirildi";
    private const string Token = "dGhpcyBpcyBub3QgYSByZWFsIHRva2VuIGF0IGFsbA-_";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheLink_GoesToTheNewAddress_NotTheCurrentOne()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var (current, next) = (NewAddress("current"), NewAddress("next"));
        await RegisterAsync(services, user, current);
        var requested = new EmailChangeRequested(user, Guid.CreateVersion7(), Token, next, DateTimeOffset.UtcNow.AddHours(24), DateTimeOffset.UtcNow);

        Assert.True((await DeliverAsync(services, requested)).IsSuccess);
        Assert.True((await DeliverAsync(services, requested)).IsSuccess);

        var queued = Assert.Single(await EmailsAsync(services, user, EmailKind.EmailChangeVerification));
        Assert.Equal((requested.TokenId.ToString("N"), next), (queued.SourceKey, queued.ToAddress));
        Assert.Contains($"https://app.netboxplus.test/change-email?token={Token}", queued.TextBody, StringComparison.Ordinal);
        Assert.Contains("24 saat geçerli", queued.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        var received = await mailpit.FindAsync(next, LinkSubject, Cancellation);
        Assert.Contains($"https://app.netboxplus.test/change-email?token={Token}", received!.Html, StringComparison.Ordinal);
        var sent = Assert.Single(await EmailsAsync(services, user, EmailKind.EmailChangeVerification));
        Assert.Equal((EmailStatus.Sent, (string?)null, (string?)null), (sent.Status, sent.ToAddress, sent.HtmlBody));
    }

    [Fact]
    public async Task AnExpiredLink_IsNotEmailed()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        var result = await DeliverAsync(services, new EmailChangeRequested(user, Guid.CreateVersion7(), Token, "next@example.com", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));

        Assert.True(result.IsSuccess);
        Assert.Empty(await EmailsAsync(services, user, EmailKind.EmailChangeVerification));
    }

    [Fact]
    public async Task AChange_MovesTheRecipient_AndTellsTheOldAddressOnce()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var (old, next) = (NewAddress("old"), NewAddress("next"));
        await RegisterAsync(services, user, old);
        var changedAt = new DateTimeOffset(2026, 11, 15, 18, 30, 0, TimeSpan.Zero);
        var changed = new EmailChanged(user, next, 2, changedAt, DateTimeOffset.UtcNow);

        Assert.True((await DeliverAsync(services, changed)).IsSuccess);
        Assert.True((await DeliverAsync(services, changed)).IsSuccess);
        Assert.True((await DeliverAsync(services, changed with { Email = NewAddress("older"), Revision = 1 })).IsSuccess);

        var recipient = await RecipientAsync(services, user);
        Assert.Equal((next, 2), (recipient.Email, recipient.EmailRevision));
        var notice = Assert.Single(await EmailsAsync(services, user, EmailKind.EmailChanged));
        Assert.Equal(($"{user:N}-2", old), (notice.SourceKey, notice.ToAddress));
        Assert.Contains("15.11.2026 18:30 (UTC)", notice.TextBody, StringComparison.Ordinal);
        Assert.Contains(string.Concat(next.AsSpan(0, 1), "***", next.AsSpan(next.IndexOf('@', StringComparison.Ordinal))), notice.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(next, notice.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        Assert.NotNull(await mailpit.FindAsync(old, NoticeSubject, Cancellation));
    }

    [Fact]
    public async Task AChangeBeforeTheRegistration_MakesTheRecipient_AndTheRegistrationStillWelcomes()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        Assert.True((await DeliverAsync(services, new EmailChanged(user, "moved@example.com", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).IsSuccess);
        await RegisterAsync(services, user, "registered@example.com");

        var recipient = await RecipientAsync(services, user);
        Assert.Equal(("moved@example.com", 2), (recipient.Email, recipient.EmailRevision));
        Assert.Empty(await EmailsAsync(services, user, EmailKind.EmailChanged));
        Assert.Single(await EmailsAsync(services, user, EmailKind.Welcome));
    }

    [Fact]
    public async Task AChangeWithoutAnAddress_IsRefused()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await RegisterAsync(services, user, "kept@example.com");

        var result = await DeliverAsync(services, new EmailChanged(user, " ", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        Assert.True(result.IsFailure);
        Assert.Equal("kept@example.com", (await RecipientAsync(services, user)).Email);
        Assert.Empty(await EmailsAsync(services, user, EmailKind.EmailChanged));
    }

    [Theory]
    [InlineData(typeof(IIntegrationEventHandler<EmailChangeRequested>), "Notification.Application.Emails.QueueEmailChange.QueueEmailChangeOnRequested")]
    [InlineData(typeof(IIntegrationEventHandler<EmailChanged>), "Notification.Application.Recipients.ChangeRecipientEmail.ChangeRecipientEmailOnEmailChanged")]
    public void TheHandlers_KeepTheirInboxKeys(Type handlerType, string fullName)
    {
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(handlerType.IsAssignableFrom);

        Assert.Equal(fullName, type.FullName);
    }

    private Task<ServiceProvider> CreateServicesAsync()
        => database.CreateServicesAsync(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = mailpit.SmtpHost,
            ["Email:Smtp:Port"] = mailpit.SmtpPort.ToString(CultureInfo.InvariantCulture),
        });

    private static string NewAddress(string label) => $"{label}-{Guid.NewGuid():N}@{MailpitFixture.AcceptedDomain}";

    private static async Task<Result> DeliverAsync<T>(ServiceProvider services, T message)
        where T : class
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<T>>());
        Result? result = null;
        await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            Guid.NewGuid(),
            handler.GetType().FullName!,
            async token => result = await handler.Handle(message, token),
            Cancellation);
        return result!;
    }

    private static async Task RegisterAsync(ServiceProvider services, Guid user, string address)
        => Assert.True((await DeliverAsync(services, new UserRegistered(user, address, DateTimeOffset.UtcNow))).IsSuccess);

    private static async Task SendDueAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Cancellation);
    }

    private static async Task<Notification.Domain.Recipients.Recipient> RecipientAsync(ServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Recipients.AsNoTracking().SingleAsync(recipient => recipient.Id == user, Cancellation);
    }

    private static async Task<List<OutgoingEmail>> EmailsAsync(ServiceProvider services, Guid user, EmailKind kind)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking()
            .Where(email => email.UserId == user && email.Kind == kind)
            .ToListAsync(Cancellation);
    }
}
