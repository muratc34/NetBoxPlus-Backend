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

/// <summary>A deleted account as the messaging layer delivers it: one last e-mail, then nothing.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class AccountDeletionEmailTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string Subject = "Hesabın silindi";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ADeletion_SendsOneLastEmailToTheAddress_ThenForgetsIt()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var address = $"leaving-{Guid.NewGuid():N}@{MailpitFixture.AcceptedDomain}";
        await DeliverAsync(services, new UserRegistered(user, address, DateTimeOffset.UtcNow));
        await SendDueAsync(services);
        await DeliverAsync(services, new EmailVerificationRequested(user, Guid.CreateVersion7(), "token-value", DateTimeOffset.UtcNow.AddHours(24), DateTimeOffset.UtcNow));
        var deletedAt = new DateTimeOffset(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

        Assert.True((await DeliverAsync(services, new UserDeleted(user, deletedAt, deletedAt))).IsSuccess);
        Assert.True((await DeliverAsync(services, new UserDeleted(user, deletedAt, deletedAt))).IsSuccess);

        var recipient = await RecipientAsync(services, user);
        Assert.Equal(($"deleted-{user:N}@deleted.invalid", (DateTimeOffset?)deletedAt), (recipient.Email, recipient.DeletedAt));
        var emails = await EmailsAsync(services, user);
        Assert.DoesNotContain(emails, email => email.Kind == EmailKind.EmailVerification);
        Assert.All(emails.Where(email => email.Kind != EmailKind.AccountDeleted), email => Assert.Null(email.HtmlBody));
        var goodbye = Assert.Single(emails, email => email.Kind == EmailKind.AccountDeleted);
        Assert.Equal(address, goodbye.ToAddress);
        Assert.Contains("16.11.2026 09:30 (UTC)", goodbye.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        Assert.NotNull(await mailpit.FindAsync(address, Subject, Cancellation));
        var sent = Assert.Single(await EmailsAsync(services, user), email => email.Kind == EmailKind.AccountDeleted);
        Assert.Equal((EmailStatus.Sent, (string?)null, (string?)null), (sent.Status, sent.ToAddress, sent.HtmlBody));
    }

    [Fact]
    public async Task AnEmailQueuedAfterTheDeletion_GoesNowhere()
    {
        await using var services = await CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await DeliverAsync(services, new UserRegistered(user, "gone@example.com", DateTimeOffset.UtcNow));
        await DeliverAsync(services, new UserDeleted(user, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await DeliverAsync(services, new EmailVerificationRequested(user, Guid.CreateVersion7(), "token-value", DateTimeOffset.UtcNow.AddHours(24), DateTimeOffset.UtcNow));
        await MarkGoodbyeSentAsync(services, user);

        await SendDueAsync(services);

        var late = Assert.Single(await EmailsAsync(services, user), email => email.Kind == EmailKind.EmailVerification);
        Assert.Equal((EmailStatus.Failed, EmailDispatcher.RecipientDeleted, (string?)null), (late.Status, late.LastError, late.HtmlBody));
    }

    [Fact]
    public async Task ADeletionBeforeTheRegistration_KeepsTheAddressOut_AndSendsNothing()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        Assert.True((await DeliverAsync(services, new UserDeleted(user, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).IsSuccess);
        Assert.True((await DeliverAsync(services, new UserRegistered(user, "late@example.com", DateTimeOffset.UtcNow))).IsSuccess);
        Assert.True((await DeliverAsync(services, new EmailChanged(user, "later@example.com", 2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).IsSuccess);

        var recipient = await RecipientAsync(services, user);
        Assert.Equal($"deleted-{user:N}@deleted.invalid", recipient.Email);
        Assert.NotNull(recipient.DeletedAt);
        Assert.Empty(await EmailsAsync(services, user));
    }

    [Fact]
    public void TheHandler_KeepsItsInboxKey()
    {
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(typeof(IIntegrationEventHandler<UserDeleted>).IsAssignableFrom);

        Assert.Equal("Notification.Application.Recipients.ForgetRecipient.ForgetRecipientOnUserDeleted", type.FullName);
    }

    private Task<ServiceProvider> CreateServicesAsync()
        => database.CreateServicesAsync(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = mailpit.SmtpHost,
            ["Email:Smtp:Port"] = mailpit.SmtpPort.ToString(CultureInfo.InvariantCulture),
        });

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

    private static async Task SendDueAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Cancellation);
    }

    /// <summary>Takes the goodbye out of the way, so that only the late e-mail is due.</summary>
    private static async Task MarkGoodbyeSentAsync(ServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var goodbye = await context.Emails.SingleAsync(email => email.UserId == user && email.Kind == EmailKind.AccountDeleted, Cancellation);
        goodbye.MarkSent(DateTimeOffset.UtcNow);
        await context.SaveChangesAsync(Cancellation);
    }

    private static async Task<Notification.Domain.Recipients.Recipient> RecipientAsync(ServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Recipients.AsNoTracking().SingleAsync(recipient => recipient.Id == user, Cancellation);
    }

    private static async Task<List<OutgoingEmail>> EmailsAsync(ServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking()
            .Where(email => email.UserId == user)
            .ToListAsync(Cancellation);
    }
}
