using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Domain.Emails;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.IntegrationTests.Email;

/// <summary>The account service's verification tokens, turned into e-mails as the messaging layer delivers them.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class EmailVerificationEmailTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string Subject = "E-posta adresini doğrula";

    /// <summary>A real token's shape, with the characters a link must escape replaced by URL-safe ones.</summary>
    private const string Token = "dGhpcyBpcyBub3QgYSByZWFsIHRva2VuIGF0IGFsbA-_";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AToken_QueuesOneEmailWithTheLink_AndItIsSentOnceTheAddressIsKnown()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await database.CreateServicesAsync(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = mailpit.SmtpHost,
            ["Email:Smtp:Port"] = mailpit.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        var (user, address) = (Guid.CreateVersion7(), $"viewer-{Guid.NewGuid():N}@{MailpitFixture.AcceptedDomain}");
        var requested = Requested(user, DateTimeOffset.UtcNow.AddHours(24));

        Assert.True((await DeliverAsync(services, Guid.NewGuid(), requested)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, Guid.NewGuid(), requested)).Result!.IsSuccess);

        var queued = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailKind.EmailVerification, requested.TokenId.ToString("N"), Subject), (queued.Kind, queued.SourceKey, queued.Subject));
        Assert.Contains($"href=\"https://app.netboxplus.test/verify-email?token={Token}\"", queued.HtmlBody, StringComparison.Ordinal);
        Assert.Contains($"E-postamı doğrula: https://app.netboxplus.test/verify-email?token={Token}", queued.TextBody, StringComparison.Ordinal);
        Assert.Contains("24 saat geçerli", queued.TextBody, StringComparison.Ordinal);

        // The registration has not arrived yet: the e-mail waits for the address.
        await SendDueAsync(services);
        Assert.Equal(EmailStatus.Pending, Assert.Single(await EmailsAsync(services, user)).Status);

        await RegisterAsync(services, user, address);
        await MakeDueAsync(services, queued.Id);
        await SendDueAsync(services);

        var received = await mailpit.FindAsync(address, Subject, Cancellation);
        Assert.Contains($"https://app.netboxplus.test/verify-email?token={Token}", received!.Html, StringComparison.Ordinal);
        var sent = (await EmailsAsync(services, user)).Single(email => email.Kind == EmailKind.EmailVerification);
        Assert.Equal((EmailStatus.Sent, (string?)null), (sent.Status, sent.HtmlBody));
    }

    [Fact]
    public async Task AnExpiredToken_IsNotEmailed()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        var (processed, result) = await DeliverAsync(services, Guid.NewGuid(), Requested(user, DateTimeOffset.UtcNow.AddSeconds(-1)));

        Assert.True(processed);
        Assert.True(result!.IsSuccess);
        Assert.Empty(await EmailsAsync(services, user));
    }

    [Fact]
    public async Task ALinkWithLittleTimeLeft_SaysAtLeastAnHour()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        await DeliverAsync(services, Guid.NewGuid(), Requested(user, DateTimeOffset.UtcNow.AddMinutes(10)));

        Assert.Contains("1 saat geçerli", Assert.Single(await EmailsAsync(services, user)).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHandler_KeepsItsInboxKey()
    {
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(candidate => typeof(IIntegrationEventHandler<EmailVerificationRequested>).IsAssignableFrom(candidate));

        Assert.Equal("Notification.Application.Emails.QueueEmailVerification.QueueEmailVerificationOnRequested", type.FullName);
    }

    private static EmailVerificationRequested Requested(Guid user, DateTimeOffset expiresAt)
        => new(user, Guid.CreateVersion7(), Token, expiresAt, DateTimeOffset.UtcNow);

    private static async Task<(bool Processed, Result? Result)> DeliverAsync(ServiceProvider services, Guid messageId, EmailVerificationRequested requested)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<EmailVerificationRequested>>());
        Result? result = null;
        var processed = await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            messageId,
            handler.GetType().FullName!,
            async token => result = await handler.Handle(requested, token),
            Cancellation);
        return (processed, result);
    }

    private static async Task RegisterAsync(ServiceProvider services, Guid user, string address)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<UserRegistered>>());
        await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            Guid.NewGuid(), handler.GetType().FullName!, token => handler.Handle(new UserRegistered(user, address, DateTimeOffset.UtcNow), token), Cancellation);
    }

    private static async Task SendDueAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Cancellation);
    }

    private static async Task MakeDueAsync(ServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE \"OutgoingEmails\" SET \"NextAttemptAt\" = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE \"Id\" = {id}", Cancellation);
    }

    private static async Task<List<OutgoingEmail>> EmailsAsync(ServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking()
            .Where(email => email.UserId == user && email.Kind == EmailKind.EmailVerification)
            .ToListAsync(Cancellation);
    }
}
