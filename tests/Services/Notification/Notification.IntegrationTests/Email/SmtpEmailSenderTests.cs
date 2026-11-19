using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Application.Abstractions;
using Notification.Infrastructure;

namespace Notification.IntegrationTests.Email;

[Collection(nameof(MailpitCollection))]
public sealed class SmtpEmailSenderTests(MailpitFixture mailpit)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnEmail_ArrivesWithItsSenderSubjectAndBothBodies()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);
        var to = $"viewer-{Guid.NewGuid():N}@{MailpitFixture.AcceptedDomain}";

        var delivery = await Sender(mailpit.SmtpHost, mailpit.SmtpPort).SendAsync(new EmailMessage(to, "Hoş geldin", "<p>Merhaba <b>izleyici</b></p>", "Merhaba izleyici"), Cancellation);

        Assert.Equal(EmailDelivery.Sent, delivery);
        var received = await mailpit.FindAsync(to, Cancellation);
        Assert.NotNull(received);
        Assert.Equal(("NetBoxPlus", "no-reply@netboxplus.test"), (received.From.Name, received.From.Email));
        Assert.Equal(to, Assert.Single(received.To).Email);
        Assert.Equal("Hoş geldin", received.Subject);
        Assert.Contains("<b>izleyici</b>", received.Html, StringComparison.Ordinal);
        Assert.Equal("Merhaba izleyici", received.Text.Trim());
    }

    [Fact]
    public async Task ARefusedRecipient_IsRejected_NotTriedAgain()
    {
        Assert.SkipUnless(mailpit.IsAvailable, mailpit.SkipReason);

        var delivery = await Sender(mailpit.SmtpHost, mailpit.SmtpPort).SendAsync(new EmailMessage("someone@refused.test", "Konu", "<p>x</p>", "x"), Cancellation);

        Assert.Equal(EmailDeliveryOutcome.Rejected, delivery.Outcome);
        Assert.StartsWith("SMTP 5", delivery.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AServerThatIsNotThere_IsTriedAgain()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var delivery = await Sender("127.0.0.1", port).SendAsync(new EmailMessage("viewer@example.com", "Konu", "<p>x</p>", "x"), Cancellation);

        Assert.Equal(EmailDeliveryOutcome.TryAgain, delivery.Outcome);
    }

    private static IEmailSender Sender(string host, int port)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Smtp",
            ["Email:From"] = "NetBoxPlus <no-reply@netboxplus.test>",
            ["Email:Smtp:Host"] = host,
            ["Email:Smtp:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddEmail(configuration);
        return services.BuildServiceProvider().GetRequiredService<IEmailSender>();
    }
}
