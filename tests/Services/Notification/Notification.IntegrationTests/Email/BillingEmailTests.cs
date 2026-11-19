using System.Text.Json;
using Auth.Contracts;
using Billing.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Domain.Emails;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Shared.Application.Messaging;
using Shared.Domain;

namespace Notification.IntegrationTests.Email;

/// <summary>The billing service's subscription events, turned into the customer's e-mails.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class BillingEmailTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private const string StartedSubject = "Aboneliğin başladı";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 30, 0, TimeSpan.FromHours(3));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AStart_IsEmailedOnce_WithThePlanAndTheNextPayment()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        await using var services = await CreateServicesAsync();
        var (user, address) = await RegisterAsync(services);
        var started = Activated(user, revision: 1, "Started");

        Assert.True((await DeliverAsync(services, started)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, started)).Result!.IsSuccess);

        var email = Assert.Single(await EmailsAsync(services, user, EmailKind.SubscriptionStarted));
        Assert.Equal(($"{started.SubscriptionId:N}-1", StartedSubject), (email.SourceKey, email.Subject));
        Assert.Contains("Planın: HD görüntü, aynı anda 2 ekran.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Sonraki ödeme 14.12.2026 tarihinde alınacak.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("İzlemeye başla: https://app.netboxplus.test/", email.TextBody, StringComparison.Ordinal);

        await SendDueAsync(services);

        Assert.NotNull(await mailpit.FindAsync(address, StartedSubject, Cancellation));
    }

    [Fact]
    public async Task AStartWithoutAKnownPeriodEnd_LeavesTheNextPaymentOut()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();

        await DeliverAsync(services, Activated(user, revision: 1, "Started") with { CurrentPeriodEnd = null, VideoQuality = "Uhd", MaxScreens = 4 });

        var text = Assert.Single(await EmailsAsync(services, user, EmailKind.SubscriptionStarted)).TextBody;
        Assert.Contains("Ultra HD görüntü, aynı anda 4 ekran", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sonraki ödeme", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AResume_IsEmailed()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var resumed = Activated(user, revision: 3, "Resumed");

        await DeliverAsync(services, resumed);

        var email = Assert.Single(await EmailsAsync(services, user, EmailKind.SubscriptionResumed));
        Assert.Equal(($"{resumed.SubscriptionId:N}-3", "Ödemen alındı"), (email.SourceKey, email.Subject));
        Assert.Contains("İzlemeye devam et: https://app.netboxplus.test/", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APlanChange_AReplay_AMessageWithoutAReason_AndASuspension_SendNothing()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var subscription = Guid.CreateVersion7();

        var results = new[]
        {
            await DeliverAsync(services, Activated(user, 4, "PlanChanged")),
            await DeliverAsync(services, Activated(user, 5, null) with { Replayed = true }),
            await DeliverAsync(services, Activated(user, 6, "Started") with { Replayed = true }),
            await DeliverAsync(services, new SubscriptionCanceled(subscription, user, Now, 7, Now, Replayed: true)),
        };

        Assert.All(results, result => Assert.True(result.Result!.IsSuccess));
        Assert.DoesNotContain(
            typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly.GetTypes(),
            typeof(IIntegrationEventHandler<SubscriptionSuspended>).IsAssignableFrom);
        Assert.Empty(await EmailsAsync(services, user, kind: null));
    }

    [Fact]
    public async Task AMessageWrittenBeforeTheReasonExisted_SendsNothing()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var old = JsonSerializer.Serialize(
            new { subscriptionId = Guid.CreateVersion7(), userId = user, planId = Guid.CreateVersion7(), videoQuality = "Hd", maxScreens = 2, currentPeriodEnd = (DateTimeOffset?)null, revision = 1, occurredAt = Now },
            IntegrationEvents.SerializerOptions);

        var message = JsonSerializer.Deserialize<SubscriptionActivated>(old, IntegrationEvents.SerializerOptions)!;
        await DeliverAsync(services, message);

        Assert.Equal(((string?)null, false), (message.Reason, message.Replayed));
        Assert.Empty(await EmailsAsync(services, user, kind: null));
    }

    [Theory]
    [InlineData(14999L, "149,99 TL")]
    [InlineData(124999L, "1.249,99 TL")]
    [InlineData(5L, "0,05 TL")]
    public async Task AFailedPayment_IsEmailedOncePerInvoice_WithTheAmount(long amountMinor, string shown)
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var failed = new PaymentFailed(Guid.CreateVersion7(), Guid.CreateVersion7(), user, amountMinor, "TRY", Now);

        Assert.True((await DeliverAsync(services, failed)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, failed)).Result!.IsSuccess);

        var email = Assert.Single(await EmailsAsync(services, user, EmailKind.PaymentFailed));
        Assert.Equal((failed.InvoiceId.ToString("N"), "Ödemen alınamadı"), (email.SourceKey, email.Subject));
        Assert.Contains($"Aboneliğinin {shown} tutarındaki ödemesi alınamadı.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Ödeme bilgilerini güncelle: https://app.netboxplus.test/account/subscription", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEnd_IsEmailed_WithItsDate()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        // 01:30 in Istanbul is still the previous day in UTC.
        var endedAt = new DateTimeOffset(2026, 11, 15, 1, 30, 0, TimeSpan.FromHours(3));
        var canceled = new SubscriptionCanceled(Guid.CreateVersion7(), user, endedAt, 5, Now);

        Assert.True((await DeliverAsync(services, canceled)).Result!.IsSuccess);
        Assert.True((await DeliverAsync(services, canceled)).Result!.IsSuccess);

        var email = Assert.Single(await EmailsAsync(services, user, EmailKind.SubscriptionEnded));
        Assert.Equal(($"{canceled.SubscriptionId:N}-5", "Aboneliğin sona erdi"), (email.SourceKey, email.Subject));
        Assert.Contains("NetBoxPlus aboneliğin 14.11.2026 tarihinde sona erdi.", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Planları gör: https://app.netboxplus.test/plans", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStartAndAResumeOfOneSubscription_AreTwoEmails()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        var subscription = Guid.CreateVersion7();

        await DeliverAsync(services, Activated(user, 1, "Started") with { SubscriptionId = subscription });
        await DeliverAsync(services, Activated(user, 3, "Resumed") with { SubscriptionId = subscription });
        await DeliverAsync(services, Activated(user, 5, "Resumed") with { SubscriptionId = subscription });

        Assert.Single(await EmailsAsync(services, user, EmailKind.SubscriptionStarted));
        Assert.Equal(2, (await EmailsAsync(services, user, EmailKind.SubscriptionResumed)).Count);
    }

    [Theory]
    [InlineData(typeof(SubscriptionActivated))]
    [InlineData(typeof(PaymentFailed))]
    [InlineData(typeof(SubscriptionCanceled))]
    public void TheHandler_KeepsItsInboxKey(Type message)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(message);
        var type = typeof(Notification.Application.NotificationApplicationServiceCollectionExtensions).Assembly
            .GetTypes().Single(handlerType.IsAssignableFrom);

        Assert.Equal("Notification.Application.Emails.QueueBillingEmails.QueueBillingEmails", type.FullName);
    }

    private static SubscriptionActivated Activated(Guid user, int revision, string? reason)
        => new(Guid.CreateVersion7(), user, Guid.CreateVersion7(), "Hd", 2, new DateTimeOffset(2026, 12, 14, 21, 0, 0, TimeSpan.Zero), revision, Now, reason);

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

    private static async Task<List<OutgoingEmail>> EmailsAsync(ServiceProvider services, Guid user, EmailKind? kind)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking()
            .Where(email => email.UserId == user && email.Kind != EmailKind.Welcome && (kind == null || email.Kind == kind))
            .ToListAsync(Cancellation);
    }
}
