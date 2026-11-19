using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Notification.Application.Abstractions;

namespace Notification.Infrastructure.Email;

/// <summary>Sends over SMTP, one connection per e-mail: they are few, and a broken connection never lingers.</summary>
internal sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        var sender = settings.Sender!;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(sender.DisplayName, sender.Address));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var security = settings.Smtp.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
            await client.ConnectAsync(settings.Smtp.Host!, settings.Smtp.Port, security, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(settings.Smtp.UserName))
            {
                await client.AuthenticateAsync(settings.Smtp.UserName, settings.Smtp.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }

            await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
            return EmailDelivery.Sent;
        }
        catch (SmtpCommandException exception)
        {
            // 4xx is the server asking to come back; 5xx is a refusal that will not change.
            var code = (int)exception.StatusCode;
            LogRefused(logger, code, exception.ErrorCode.ToString());
            return code is >= 400 and < 500 ? EmailDelivery.TryAgain($"SMTP {code}") : EmailDelivery.Rejected($"SMTP {code}");
        }
        catch (AuthenticationException)
        {
            LogRefused(logger, 535, "Authentication");
            return EmailDelivery.Rejected("SMTP authentication failed");
        }
        catch (Exception exception) when (exception is SocketException or IOException or SmtpProtocolException or ServiceNotConnectedException or TimeoutException)
        {
            LogUnreachable(logger, exception.GetType().Name);
            return EmailDelivery.TryAgain(exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SMTP server refused an e-mail with {StatusCode} ({ErrorCode})")]
    private static partial void LogRefused(ILogger logger, int statusCode, string errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SMTP server could not be reached ({Reason})")]
    private static partial void LogUnreachable(ILogger logger, string reason);
}
