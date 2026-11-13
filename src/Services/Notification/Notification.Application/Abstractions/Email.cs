namespace Notification.Application.Abstractions;

/// <summary>One e-mail to one address, with an HTML and a plain text body.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

public enum EmailDeliveryOutcome
{
    Sent,

    /// <summary>The provider could not be reached or asked to come back later: trying again may work.</summary>
    TryAgain,

    /// <summary>The provider refused the e-mail for good (a bad address, a rejected sender): trying again will not help.</summary>
    Rejected,
}

/// <param name="Detail">Why it was not sent, for the record; never the address or the content.</param>
public sealed record EmailDelivery(EmailDeliveryOutcome Outcome, string? Detail = null)
{
    public static readonly EmailDelivery Sent = new(EmailDeliveryOutcome.Sent);

    public static EmailDelivery TryAgain(string detail) => new(EmailDeliveryOutcome.TryAgain, detail);

    public static EmailDelivery Rejected(string detail) => new(EmailDeliveryOutcome.Rejected, detail);
}

/// <summary>Hands e-mail to the provider. Never throws for the provider's answers: they come back as an outcome.</summary>
public interface IEmailSender
{
    Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
