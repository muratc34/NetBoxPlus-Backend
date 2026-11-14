using Auth.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notification.Domain.Emails;
using Notification.Infrastructure.Emails;
using Notification.Infrastructure.Persistence;
using Shared.Application.Messaging;

namespace Notification.IntegrationTests.Email;

/// <summary>The queue and the worker that sends it, against PostgreSQL and a real Mailpit.</summary>
[Collection(nameof(NotificationDatabaseCollection))]
public sealed class EmailSendingTests(NotificationDatabaseFixture database, MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<ServiceProvider> WithMailpitAsync()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        return database.CreateServicesAsync(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = mailpit.SmtpHost,
            ["Email:Smtp:Port"] = mailpit.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    private static string NewAddress() => $"viewer-{Guid.NewGuid():N}@{MailpitFixture.AcceptedDomain}";

    [Fact]
    public async Task ARegistration_QueuesTheWelcome_AndTheWorkerSendsIt()
    {
        await using var services = await WithMailpitAsync();
        var (user, address) = (Guid.CreateVersion7(), NewAddress());
        await RegisterAsync(services, user, address);
        await RegisterAsync(services, user, address);

        var queued = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailKind.Welcome, EmailStatus.Pending, user.ToString("N")), (queued.Kind, queued.Status, queued.SourceKey));
        Assert.Contains("href=\"https://app.netboxplus.test/\"", queued.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("NetBoxPlus&#39;a git", queued.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("NetBoxPlus'a git: https://app.netboxplus.test/", queued.TextBody, StringComparison.Ordinal);

        Assert.Equal(1, await SendDueAsync(services));

        var received = await mailpit.FindAsync(address, Cancellation);
        Assert.Equal("NetBoxPlus'a hoş geldin", received!.Subject);
        Assert.Contains("https://app.netboxplus.test/", received.Html, StringComparison.Ordinal);
        var sent = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailStatus.Sent, 1, (string?)null, (string?)null), (sent.Status, sent.Attempts, sent.HtmlBody, sent.TextBody));
        Assert.NotNull(sent.SentAt);
        Assert.Equal(0, await SendDueAsync(services));
    }

    [Fact]
    public async Task AnSmtpServerOutOfReach_LeavesTheEmailWaiting_ForItsNextAttempt()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await RegisterAsync(services, user, NewAddress());
        var before = DateTimeOffset.UtcNow;

        Assert.Equal(1, await SendDueAsync(services));

        var email = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailStatus.Pending, 1), (email.Status, email.Attempts));
        Assert.InRange(email.NextAttemptAt, before.AddSeconds(55), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.NotNull(email.LastError);
        Assert.NotNull(email.HtmlBody);
        Assert.Null(await LeaseAsync(services, email.Id));
        Assert.Equal(0, await SendDueAsync(services));
    }

    [Fact]
    public async Task ARefusedAddress_FailsAtOnce()
    {
        await using var services = await WithMailpitAsync();
        var user = Guid.CreateVersion7();
        await RegisterAsync(services, user, $"someone-{Guid.NewGuid():N}@refused.test");

        await SendDueAsync(services);

        var email = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailStatus.Failed, 1), (email.Status, email.Attempts));
        Assert.StartsWith("SMTP 5", email.LastError, StringComparison.Ordinal);
        Assert.NotNull(email.HtmlBody);
    }

    [Fact]
    public async Task AnEmailBeforeItsAddress_Waits_ThenGoesOnceTheAddressCame()
    {
        await using var services = await WithMailpitAsync();
        var (user, address) = (Guid.CreateVersion7(), NewAddress());
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            context.Emails.Add(OutgoingEmail.Queue(user, EmailKind.Welcome, user.ToString("N"), new EmailContent("Konu", "<p>x</p>", "x"), DateTimeOffset.UtcNow).Value);
            await context.SaveChangesAsync(Cancellation);
        }

        await SendDueAsync(services);
        var waiting = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailStatus.Pending, EmailDispatcher.NoAddressYet), (waiting.Status, waiting.LastError));

        await RegisterAsync(services, user, address);
        await MakeDueAsync(services, waiting.Id);
        await SendDueAsync(services);

        Assert.Equal(EmailStatus.Sent, Assert.Single(await EmailsAsync(services, user)).Status);
        Assert.Equal("Konu", (await mailpit.FindAsync(address, Cancellation))!.Subject);
    }

    [Fact]
    public async Task TheLastAttempt_GivesUp()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await RegisterAsync(services, user, NewAddress());
        var id = Assert.Single(await EmailsAsync(services, user)).Id;
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE \"OutgoingEmails\" SET \"Attempts\" = {OutgoingEmail.MaxAttempts - 1} WHERE \"Id\" = {id}", Cancellation);
        }

        await SendDueAsync(services);

        var email = Assert.Single(await EmailsAsync(services, user));
        Assert.Equal((EmailStatus.Failed, OutgoingEmail.MaxAttempts), (email.Status, email.Attempts));
    }

    [Fact]
    public async Task TwoWorkers_NeverSendTheSameEmail()
    {
        await using var services = await WithMailpitAsync();
        var (user, address) = (Guid.CreateVersion7(), NewAddress());
        await RegisterAsync(services, user, address);

        var taken = await Task.WhenAll(SendDueAsync(services), SendDueAsync(services), SendDueAsync(services));

        Assert.Equal(1, taken.Sum());
        await Task.Delay(500, Cancellation);
        Assert.Equal(1, await mailpit.CountAsync(address, Cancellation));
    }

    [Fact]
    public async Task ALeasedEmail_IsLeftAlone_UntilTheLeaseRunsOut()
    {
        await using var services = await database.CreateServicesAsync();
        var user = Guid.CreateVersion7();
        await RegisterAsync(services, user, NewAddress());
        var id = Assert.Single(await EmailsAsync(services, user)).Id;

        await SetLeaseAsync(services, id, DateTimeOffset.UtcNow.AddMinutes(4));
        Assert.Equal(0, await SendDueAsync(services));

        await SetLeaseAsync(services, id, DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(1, await SendDueAsync(services));
    }

    [Fact]
    public async Task AFailedEmail_KeepsItsContentForAWeek()
    {
        await using var services = await WithMailpitAsync();
        var (old, recent) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await RegisterAsync(services, old, $"a-{Guid.NewGuid():N}@refused.test");
        await RegisterAsync(services, recent, $"b-{Guid.NewGuid():N}@refused.test");
        await SendDueAsync(services);
        var oldId = Assert.Single(await EmailsAsync(services, old)).Id;
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE \"OutgoingEmails\" SET \"ModifiedAt\" = {DateTimeOffset.UtcNow.AddDays(-8)} WHERE \"Id\" = {oldId}", Cancellation);
            await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().ForgetOldFailuresAsync(Cancellation);
        }

        Assert.Null(Assert.Single(await EmailsAsync(services, old)).HtmlBody);
        Assert.NotNull(Assert.Single(await EmailsAsync(services, recent)).HtmlBody);
    }

    private static async Task RegisterAsync(ServiceProvider services, Guid user, string address)
    {
        await using var scope = services.CreateAsyncScope();
        var handler = Assert.Single(scope.ServiceProvider.GetServices<IIntegrationEventHandler<UserRegistered>>());
        await scope.ServiceProvider.GetRequiredService<IInbox>().ProcessOnceAsync(
            Guid.NewGuid(), handler.GetType().FullName!, token => handler.Handle(new UserRegistered(user, address, DateTimeOffset.UtcNow), token), Cancellation);
    }

    private static async Task<int> SendDueAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().SendDueAsync(Cancellation);
    }

    private static async Task<List<OutgoingEmail>> EmailsAsync(ServiceProvider services, Guid user)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.AsNoTracking().Where(email => email.UserId == user).ToListAsync(Cancellation);
    }

    private static async Task<DateTimeOffset?> LeaseAsync(ServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Emails.Where(email => email.Id == id)
            .Select(email => EF.Property<DateTimeOffset?>(email, OutgoingEmailConfiguration.LeasedUntil)).SingleAsync(Cancellation);
    }

    private static async Task SetLeaseAsync(ServiceProvider services, Guid id, DateTimeOffset until)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE \"OutgoingEmails\" SET \"LeasedUntil\" = {until} WHERE \"Id\" = {id}", Cancellation);
    }

    private static async Task MakeDueAsync(ServiceProvider services, Guid id)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database
            .ExecuteSqlInterpolatedAsync($"UPDATE \"OutgoingEmails\" SET \"NextAttemptAt\" = {DateTimeOffset.UtcNow.AddSeconds(-1)} WHERE \"Id\" = {id}", Cancellation);
    }
}
