using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Domain.Emails;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.IntegrationTests.Email;

/// <summary>The password reset links and password change notices of the account service, turned into e-mails.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class PasswordEmailTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string ResetSubject = "Şifreni sıfırla";
    private const string ChangedSubject = "Şifren değiştirildi";

    /// <summary>A real token's shape, with the characters a link must escape replaced by URL-safe ones.</summary>
    private const string Token = "dGhpcyBpcyBub3QgYSByZWFsIHRva2VuIGF0IGFsbA-_";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetToken_IsEmailedOnceWithTheLink()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var (user, address) = await RegisterAsync(services);
        var requested = new PasswordResetRequested(user, Guid.CreateVersion7(), Token, DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow);

        Assert.True((await DeliverAsync(services, requested)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, requested)).Result!.IsSuccess);

        var queued = Assert.Single(await EmailsAsync(services, user, EmailKind.PasswordReset));
        Assert.Equal((requested.TokenId.ToString("N"), ResetSubject), (queued.SourceKey, queued.Subject));
        Assert.Contains($"href=\"https://app.netboxplus.test/reset-password?token={Token}\"", queued.HtmlBody, StringComparison.Ordinal);
        Assert.Contains($"Şifremi sıfırla: https://app.netboxplus.test/reset-password?token={Token}", queued.TextBody, StringComparison.Ordinal);
        Assert.Contains("60 dakika geçerli", queued.TextBody, StringComparison.Ordinal);
        Assert.Contains("şifren değişmez", queued.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        var received = await mailpit.FindAsync(address, ResetSubject, Cancellation);
        Assert.Contains($"https://app.netboxplus.test/reset-password?token={Token}", received!.Html, StringComparison.Ordinal);
        Assert.Equal(EmailStatus.Sent, Assert.Single(await EmailsAsync(services, user, EmailKind.PasswordReset)).Status);
    }

    [Fact]
    public async Task AnExpiredResetToken_IsNotEmailed()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        var (processed, result) = await DeliverAsync(
            services,
            new PasswordResetRequested(user, Guid.CreateVersion7(), Token, DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow));

        Assert.True(processed);
        Assert.True(result!.IsSuccess);
        Assert.Empty(await EmailsAsync(services, user, EmailKind.PasswordReset));
    }

    [Fact]
    public async Task AResetLinkWithSecondsLeft_SaysAtLeastAMinute()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        await DeliverAsync(services, new PasswordResetRequested(user, Guid.CreateVersion7(), Token, DateTimeOffset.UtcNow.AddSeconds(20), DateTimeOffset.UtcNow));

        Assert.Contains("1 dakika geçerli", Assert.Single(await EmailsAsync(services, user, EmailKind.PasswordReset)).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APasswordChange_SendsOneNoticePerChange_HoweverLate()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var (user, address) = await RegisterAsync(services);
        var changedAt = new DateTimeOffset(2026, 11, 14, 12, 5, 0, TimeSpan.FromHours(3));
        var first = new PasswordChanged(user, Guid.CreateVersion7(), changedAt, changedAt);
        var second = new PasswordChanged(user, Guid.CreateVersion7(), changedAt.AddDays(1), changedAt.AddDays(1));

        Assert.True((await DeliverAsync(services, first)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, first)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, second)).Result!.IsSuccess);

        var notices = await EmailsAsync(services, user, EmailKind.PasswordChanged);
        Assert.Equal(2, notices.Count);
        var notice = notices.Single(email => email.SourceKey == first.ChangeId.ToString("N"));
        Assert.Equal(ChangedSubject, notice.Subject);
        Assert.Contains("14.11.2026 09:05 (UTC)", notice.TextBody, StringComparison.Ordinal);
        Assert.Contains("Şifremi sıfırla: https://app.netboxplus.test/forgot-password", notice.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        var received = await mailpit.FindAsync(address, ChangedSubject, Cancellation);
        Assert.NotNull(received);

        // An e-mail about the account itself cannot be turned off: it offers no one-click unsubscribe.
        Assert.False((await mailpit.HeadersAsync(received, Cancellation)).ContainsKey("List-Unsubscribe"));
    }

    [Theory]
    [InlineData(typeof(PasswordResetRequested), "Notification.Application.Emails.QueuePasswordReset.QueuePasswordResetOnRequested")]
    [InlineData(typeof(PasswordChanged), "Notification.Application.Emails.QueuePasswordChanged.QueuePasswordChangedOnChanged")]
    public void TheHandlers_KeepTheirInboxKeys(Type message, string inboxKey)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(message);
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(handlerType.IsAssignableFrom);

        Assert.Equal(inboxKey, type.FullName);
    }

    private Task<ServiceProvider> CreateServicesAsync()
        => database.CreateServicesAsync(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = mailpit.SmtpHost,
            ["Email:Smtp:Port"] = mailpit.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    private static async Task<(bool Processed, Result? Result)> DeliverAsync<T>(ServiceProvider services, T message)
        where T : class
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<T>>());
        Result? result = null;
        var processed = await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            Guid.NewGuid(),
            handler.GetType().FullName!,
            async token => result = await handler.Handle(message, token),
            Cancellation);
        return (processed, result);
    }

    private static async Task<(Guid User, string Address)> RegisterAsync(ServiceProvider services)
    {
        var (user, address) = (Guid.CreateVersion7(), $"viewer-{Guid.NewGuid():N}@{MailpitFixture.AcceptedDomain}");
        await DeliverAsync(services, new UserRegistered(user, address, DateTimeOffset.UtcNow));
        return (user, address);
    }

    private static async Task SendDueAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Cancellation);
    }

    private static async Task<List<OutgoingEmail>> EmailsAsync(ServiceProvider services, Guid user, EmailKind kind)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking()
            .Where(email => email.UserId == user && email.Kind == kind)
            .ToListAsync(Cancellation);
    }
}
