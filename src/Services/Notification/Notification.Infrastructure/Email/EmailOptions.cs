using System.Net.Mail;

namespace Notification.Infrastructure.Email;

public enum EmailProvider
{
    /// <summary>Any SMTP server; locally Mailpit, which keeps what it gets.</summary>
    Smtp,

    Brevo,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;

    /// <summary>The sender, such as <c>NetBoxPlus &lt;no-reply@example.com&gt;</c>; with Brevo, one verified there.</summary>
    public string? From { get; set; }

    public SmtpSettings Smtp { get; set; } = new();

    public BrevoSettings Brevo { get; set; } = new();

    internal MailAddress? Sender => MailAddress.TryCreate(From, out var address) ? address : null;

    internal string? Problem => Provider switch
    {
        _ when Sender is null => $"{SectionName}:From must be an e-mail address.",
        EmailProvider.Smtp when string.IsNullOrWhiteSpace(Smtp.Host) || Smtp.Port is <= 0 or > 65535 => $"{SectionName}:Smtp needs a Host and a Port.",
        EmailProvider.Brevo when string.IsNullOrWhiteSpace(Brevo.ApiKey) => $"{SectionName}:Brevo:ApiKey is required.",
        EmailProvider.Smtp or EmailProvider.Brevo => null,
        _ => $"{SectionName}:Provider must be Smtp or Brevo.",
    };
}

public sealed class SmtpSettings
{
    public string? Host { get; set; }

    public int Port { get; set; } = 25;

    /// <summary>TLS from the start (port 465) rather than STARTTLS when the server offers it.</summary>
    public bool UseSsl { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }
}

public sealed class BrevoSettings
{
    public string? ApiKey { get; set; }

    public Uri BaseAddress { get; set; } = new("https://api.brevo.com/");
}
