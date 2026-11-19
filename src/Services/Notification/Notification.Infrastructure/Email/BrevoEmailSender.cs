using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Notification.Application.Abstractions;

namespace Notification.Infrastructure.Email;

/// <summary>
/// Sends through Brevo's transactional e-mail API with plain JSON. The key goes in the <c>api-key</c>
/// header of each request and is never logged.
/// </summary>
internal sealed partial class BrevoEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    public const string SendPath = "v3/smtp/email";

    public async Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var settings = options.Value;
        var sender = settings.Sender!;
        var body = new BrevoEmail(
            new BrevoAddress(sender.Address, string.IsNullOrEmpty(sender.DisplayName) ? null : sender.DisplayName),
            [new BrevoAddress(message.To, null)],
            message.Subject,
            message.HtmlBody,
            message.TextBody);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.Brevo.BaseAddress, SendPath))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("api-key", settings.Brevo.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogUnreachable(logger, exception.GetType().Name);
            return EmailDelivery.TryAgain(exception.GetType().Name);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return EmailDelivery.Sent;
            }

            var status = (int)response.StatusCode;
            LogRefused(logger, status);

            // Too many requests and the provider's own trouble pass; a bad request, key or sender does not.
            return response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500
                ? EmailDelivery.TryAgain($"Brevo {status}")
                : EmailDelivery.Rejected($"Brevo {status}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Brevo refused an e-mail with {StatusCode}")]
    private static partial void LogRefused(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Brevo could not be reached ({Reason})")]
    private static partial void LogUnreachable(ILogger logger, string reason);

    private sealed record BrevoAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name);

    private sealed record BrevoEmail(
        [property: JsonPropertyName("sender")] BrevoAddress Sender,
        [property: JsonPropertyName("to")] IReadOnlyList<BrevoAddress> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("htmlContent")] string HtmlContent,
        [property: JsonPropertyName("textContent")] string TextContent);
}
