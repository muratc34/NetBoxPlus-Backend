using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Application.SignIns;
using Notification.Domain.Emails;
using Notification.Domain.SignIns;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Notification.IntegrationTests.Email;
using Shared.Application.Cqrs;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.IntegrationTests.SignIns;

/// <summary>Sign-ins from new devices, the preference that turns their alerts off, and the passkey notices.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class SignInAlertTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string AlertSubject = "Yeni bir cihazdan giriş yapıldı";
    private const string Chrome = "Chrome · Windows";
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 9, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheRegistrationDevice_IsKnown_AndOnlyANewOneIsAlerted_OnceWithTheTurnOffHeaders()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var (user, address) = await RegisterAsync(services);

        await SignInAsync(services, user, Chrome, Start, registration: true);
        await SignInAsync(services, user, Chrome, Start.AddDays(1));
        Assert.Empty(await EmailsAsync(services, user, EmailKind.NewSignIn));

        var phone = new SignedIn(user, Guid.CreateVersion7(), "Safari · iOS", "Customer", Start.AddDays(2), false, Start.AddDays(2));
        Assert.True((await DeliverAsync(services, phone)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, phone)).Result!.IsSuccess);

        var alert = Assert.Single(await EmailsAsync(services, user, EmailKind.NewSignIn));
        Assert.Equal((phone.SessionId.ToString("N"), AlertSubject), (alert.SourceKey, alert.Subject));
        Assert.Contains("hesabına 22.11.2026 09:30 (UTC) tarihinde yeni bir cihazdan (Safari · iOS) giriş yapıldı", alert.TextBody, StringComparison.Ordinal);
        Assert.Contains("Cihazlarımı gör: https://app.netboxplus.test/account/security", alert.TextBody, StringComparison.Ordinal);
        Assert.StartsWith("https://api.netboxplus.test/notification/unsubscribe?token=", alert.UnsubscribeUrl, StringComparison.Ordinal);
        Assert.Contains("https://app.netboxplus.test/unsubscribe?token=" + TokenOf(alert), alert.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        var received = await mailpit.FindAsync(address, AlertSubject, Cancellation);
        var headers = await mailpit.HeadersAsync(received!, Cancellation);
        Assert.Equal($"<{alert.UnsubscribeUrl}>", Assert.Single(headers["List-Unsubscribe"]));
        Assert.Equal("List-Unsubscribe=One-Click", Assert.Single(headers["List-Unsubscribe-Post"]));
        Assert.Null(Assert.Single(await EmailsAsync(services, user, EmailKind.NewSignIn)).UnsubscribeUrl);
    }

    [Fact]
    public async Task ADevice_NotSeenForThirtyDays_IsNewAgain_AndALateOldSignInDoesNotMoveItBack()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await SignInAsync(services, user, Chrome, Start, registration: true);

        await SignInAsync(services, user, Chrome, Start.AddDays(29));
        await SignInAsync(services, user, Chrome, Start.AddDays(1));
        Assert.Empty(await EmailsAsync(services, user, EmailKind.NewSignIn));

        await SignInAsync(services, user, Chrome, Start.AddDays(29 + 31), admin: true);

        var alert = Assert.Single(await EmailsAsync(services, user, EmailKind.NewSignIn));
        Assert.Contains("yönetim paneline", alert.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownDevice_IsOneDevice_NamedAsUnknown()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        await SignInAsync(services, user, null, Start);
        await SignInAsync(services, user, "  ", Start.AddMinutes(5));

        var alert = Assert.Single(await EmailsAsync(services, user, EmailKind.NewSignIn));
        Assert.Contains("tanımadığımız bir cihazdan giriş yapıldı", alert.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TurnedOffAlerts_SendNothing_ButTheDeviceIsStillRemembered()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await SendAsync(services, new SetNotificationPreferencesCommand(user, false));

        await SignInAsync(services, user, Chrome, Start);
        await SendAsync(services, new SetNotificationPreferencesCommand(user, true));
        await SignInAsync(services, user, Chrome, Start.AddDays(1));

        Assert.Empty(await EmailsAsync(services, user, EmailKind.NewSignIn));
        Assert.True((await QueryAsync(services, new GetNotificationPreferencesQuery(user))).SignInAlerts);
    }

    [Fact]
    public async Task TheAlertsLink_TurnsTheAlertsOff_AgainAndAgain_UntilItExpires()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        Assert.True((await QueryAsync(services, new GetNotificationPreferencesQuery(user))).SignInAlerts);
        await SignInAsync(services, user, Chrome, Start);
        var token = TokenOf(Assert.Single(await EmailsAsync(services, user, EmailKind.NewSignIn)));

        Assert.True((await TrySendAsync(services, new UnsubscribeCommand(token))).IsSuccess);
        Assert.True((await TrySendAsync(services, new UnsubscribeCommand(token))).IsSuccess);
        Assert.False((await QueryAsync(services, new GetNotificationPreferencesQuery(user))).SignInAlerts);

        await SignInAsync(services, user, "Firefox · Linux", Start.AddDays(1));
        Assert.Single(await EmailsAsync(services, user, EmailKind.NewSignIn));

        Assert.Equal(SignInAlertErrors.UnsubscribeTokenInvalid, (await TrySendAsync(services, new UnsubscribeCommand(token + "x"))).Error);
        Assert.True((await TrySendAsync(services, new UnsubscribeCommand(null))).IsFailure);

        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().UnsubscribeLinks
                .Where(link => link.UserId == user)
                .ExecuteUpdateAsync(setters => setters.SetProperty(link => link.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Cancellation);
        }

        Assert.Equal(SignInAlertErrors.UnsubscribeTokenInvalid, (await TrySendAsync(services, new UnsubscribeCommand(token))).Error);
    }

    [Fact]
    public async Task ADeletedAccount_IsForgotten_AndToldNothingMore()
    {
        await using var services = await database.CreateServicesAsync();
        var (user, _) = await RegisterAsync(services);
        await SignInAsync(services, user, Chrome, Start);
        await SendAsync(services, new SetNotificationPreferencesCommand(user, false));

        Assert.True((await DeliverAsync(services, new UserDeleted(user, Start.AddDays(1), Start.AddDays(1)))).Result!.IsSuccess);
        await SignInAsync(services, user, "Edge · Windows", Start.AddDays(2));

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        Assert.False(await context.KnownDevices.AnyAsync(device => device.UserId == user, Cancellation));
        Assert.False(await context.Preferences.AnyAsync(preference => preference.UserId == user, Cancellation));
        Assert.False(await context.UnsubscribeLinks.AnyAsync(link => link.UserId == user, Cancellation));
        Assert.Empty(await EmailsAsync(services, user, EmailKind.NewSignIn));
    }

    [Fact]
    public async Task APasskeyChange_SendsANoticeEach_ThatCannotBeTurnedOff()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var (user, address) = await RegisterAsync(services);
        await SendAsync(services, new SetNotificationPreferencesCommand(user, false));
        var passkey = Guid.CreateVersion7();

        var added = new PasskeyAdded(user, passkey, "iPhone", Start, Start);
        Assert.True((await DeliverAsync(services, added)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, added)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, new PasskeyRemoved(user, passkey, "iPhone", Start.AddHours(1), Start.AddHours(1)))).Result!.IsSuccess);

        var notice = Assert.Single(await EmailsAsync(services, user, EmailKind.PasskeyAdded));
        Assert.Contains("\"iPhone\" adlı bir passkey eklendi", notice.TextBody, StringComparison.Ordinal);
        Assert.Null(notice.UnsubscribeUrl);
        Assert.Contains("\"iPhone\" adlı passkey silindi", Assert.Single(await EmailsAsync(services, user, EmailKind.PasskeyRemoved)).TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        var received = await mailpit.FindAsync(address, "Hesabına bir passkey eklendi", Cancellation);
        Assert.False((await mailpit.HeadersAsync(received!, Cancellation)).ContainsKey("List-Unsubscribe"));
    }

    [Theory]
    [InlineData(typeof(SignedIn), "Notification.Application.SignIns.RecordSignInOnSignedIn")]
    [InlineData(typeof(PasskeyAdded), "Notification.Application.Emails.QueuePasskeyNotice.QueuePasskeyNoticeOnPasskeyChanged")]
    [InlineData(typeof(PasskeyRemoved), "Notification.Application.Emails.QueuePasskeyNotice.QueuePasskeyNoticeOnPasskeyChanged")]
    public void TheHandlers_KeepTheirInboxKeys(Type message, string inboxKey)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(message);
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(handlerType.IsAssignableFrom);

        Assert.Equal(inboxKey, type.FullName);
    }

    private static string TokenOf(OutgoingEmail alert)
    {
        var url = alert.UnsubscribeUrl!;
        return Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
    }

    private Task<ServiceProvider> CreateServicesAsync()
        => database.CreateServicesAsync(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = mailpit.SmtpHost,
            ["Email:Smtp:Port"] = mailpit.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    private static async Task SignInAsync(ServiceProvider services, Guid user, string? device, DateTimeOffset at, bool registration = false, bool admin = false)
    {
        var signedIn = new SignedIn(user, Guid.CreateVersion7(), device, admin ? "Admin" : "Customer", at, registration, at);
        Assert.True((await DeliverAsync(services, signedIn)).Result!.IsSuccess);
    }

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

    private static async Task SendAsync(ServiceProvider services, ICommand command) => Assert.True((await TrySendAsync(services, command)).IsSuccess);

    private static async Task<Result> TrySendAsync(ServiceProvider services, ICommand command)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().Send(command, Cancellation);
    }

    private static async Task<T> QueryAsync<T>(ServiceProvider services, IQuery<T> query)
    {
        await using var scope = services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IDispatcher>().Query(query, Cancellation)).Value;
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
