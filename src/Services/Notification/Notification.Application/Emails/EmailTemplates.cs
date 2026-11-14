using System.Net;
using System.Text;
using Notification.Domain.Emails;

namespace Notification.Application.Emails;

/// <summary>
/// The e-mails, in Turkish (users have no language preference yet). Every value put into the HTML is
/// encoded; the plain text body says the same thing without markup.
/// </summary>
internal static class EmailTemplates
{
    public static EmailContent Welcome(Uri home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return Render(
            "NetBoxPlus'a hoş geldin",
            ["Hesabın hazır. Planını seçip izlemeye hemen başlayabilirsin."],
            ("NetBoxPlus'a git", home));
    }

    private static EmailContent Render(string subject, IReadOnlyList<string> paragraphs, (string Label, Uri Address)? action = null)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"tr\"><head><meta charset=\"utf-8\"><title>")
            .Append(WebUtility.HtmlEncode(subject))
            .Append("</title></head><body style=\"margin:0;padding:24px;background:#141414;font-family:Arial,Helvetica,sans-serif;color:#e5e5e5\">")
            .Append("<div style=\"max-width:560px;margin:0 auto;background:#1f1f1f;border-radius:8px;padding:32px\">")
            .Append("<h1 style=\"margin:0 0 24px;color:#e50914;font-size:24px\">NetBoxPlus</h1>")
            .Append("<h2 style=\"margin:0 0 16px;font-size:20px\">").Append(WebUtility.HtmlEncode(subject)).Append("</h2>");
        foreach (var paragraph in paragraphs)
        {
            html.Append("<p style=\"line-height:1.5\">").Append(WebUtility.HtmlEncode(paragraph)).Append("</p>");
        }

        var text = new StringBuilder().AppendLine(subject).AppendLine();
        foreach (var paragraph in paragraphs)
        {
            text.AppendLine(paragraph).AppendLine();
        }

        if (action is var (label, address))
        {
            html.Append("<p style=\"margin:32px 0\"><a href=\"").Append(WebUtility.HtmlEncode(address.AbsoluteUri))
                .Append("\" style=\"background:#e50914;color:#ffffff;padding:12px 24px;border-radius:4px;text-decoration:none\">")
                .Append(WebUtility.HtmlEncode(label)).Append("</a></p>");
            text.Append(label).Append(": ").AppendLine(address.AbsoluteUri).AppendLine();
        }

        html.Append("<p style=\"color:#8c8c8c;font-size:12px\">Bu e-postayı NetBoxPlus hesabın için gönderdik.</p></div></body></html>");
        text.Append("Bu e-postayı NetBoxPlus hesabın için gönderdik.");
        return new EmailContent(subject, html.ToString(), text.ToString());
    }
}
