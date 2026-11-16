using System.Globalization;
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

    /// <param name="validFor">How long the link still works; shown in whole hours, at least one.</param>
    public static EmailContent EmailVerification(Uri link, TimeSpan validFor)
    {
        ArgumentNullException.ThrowIfNull(link);
        var hours = Math.Max(1, (int)Math.Round(validFor.TotalHours, MidpointRounding.AwayFromZero));
        return Render(
            "E-posta adresini doğrula",
            [
                "NetBoxPlus hesabının e-posta adresini doğrulamak için aşağıdaki bağlantıyı aç.",
                $"Bağlantı {hours} saat geçerli ve yalnızca bir kez kullanılabilir. Süresi geçerse hesabından yenisini isteyebilirsin.",
                "Bu hesabı sen açmadıysan bu e-postayı yok sayabilirsin.",
            ],
            ("E-postamı doğrula", link));
    }

    /// <param name="validFor">How long the link still works; shown in whole minutes, at least one.</param>
    public static EmailContent PasswordReset(Uri link, TimeSpan validFor)
    {
        ArgumentNullException.ThrowIfNull(link);
        var minutes = Math.Max(1, (int)Math.Round(validFor.TotalMinutes, MidpointRounding.AwayFromZero));
        return Render(
            "Şifreni sıfırla",
            [
                "NetBoxPlus hesabın için şifre sıfırlama isteği aldık. Yeni şifreni belirlemek için aşağıdaki bağlantıyı aç.",
                $"Bağlantı {minutes} dakika geçerli ve yalnızca bir kez kullanılabilir. Şifren değişince tüm cihazlarda oturumun kapanır.",
                "Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin; şifren değişmez.",
            ],
            ("Şifremi sıfırla", link));
    }

    /// <param name="validFor">How long the link still works; shown in whole hours, at least one.</param>
    public static EmailContent EmailChangeVerification(Uri link, TimeSpan validFor)
    {
        ArgumentNullException.ThrowIfNull(link);
        var hours = Math.Max(1, (int)Math.Round(validFor.TotalHours, MidpointRounding.AwayFromZero));
        return Render(
            "Yeni e-posta adresini onayla",
            [
                "NetBoxPlus hesabının e-posta adresini bu adresle değiştirmek istedin. Değişikliği tamamlamak için aşağıdaki bağlantıyı aç.",
                $"Bağlantı {hours} saat geçerli ve yalnızca bir kez kullanılabilir. Bağlantıyı açana kadar hesabın eski adresinde kalır.",
                "Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin; hesap değişmez.",
            ],
            ("Adresimi onayla", link));
    }

    /// <param name="newEmail">Shown masked, so that a reader of the old mailbox learns no more than needed.</param>
    /// <param name="changedAt">Shown in UTC: users have no time zone preference yet.</param>
    /// <param name="reset">Where to reset the password if it was not the user who changed it.</param>
    public static EmailContent EmailChanged(string newEmail, DateTimeOffset changedAt, Uri reset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newEmail);
        ArgumentNullException.ThrowIfNull(reset);
        var when = changedAt.ToUniversalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        return Render(
            "E-posta adresin değiştirildi",
            [
                $"NetBoxPlus hesabının e-posta adresi {when} (UTC) tarihinde {Mask(newEmail)} olarak değiştirildi. Bundan sonraki e-postalar o adrese gidecek.",
                "Bunu sen yaptıysan başka bir şey yapmana gerek yok.",
                "Sen yapmadıysan hemen şifreni sıfırla ve bize ulaş.",
            ],
            ("Şifremi sıfırla", reset));
    }

    public static EmailContent AccountDeleted(DateTimeOffset deletedAt)
    {
        var when = deletedAt.ToUniversalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        return Render(
            "Hesabın silindi",
            [
                $"NetBoxPlus hesabın {when} (UTC) tarihinde silindi. Profillerin, izleme geçmişin ve aboneliğin kaldırıldı; aboneliğin için bundan sonra ödeme alınmayacak.",
                "Bu, sana gönderdiğimiz son e-posta. Adresin bizde kalmadı.",
                "Bunu sen yapmadıysan bize ulaş.",
            ],
            null);
    }

    /// <summary>The first letter of the local part and the whole domain: <c>m***@example.com</c>.</summary>
    internal static string Mask(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    /// <param name="changedAt">Shown in UTC: users have no time zone preference yet.</param>
    /// <param name="reset">Where to reset the password if it was not the user who changed it.</param>
    public static EmailContent PasswordChanged(DateTimeOffset changedAt, Uri reset)
    {
        ArgumentNullException.ThrowIfNull(reset);
        var when = changedAt.ToUniversalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        return Render(
            "Şifren değiştirildi",
            [
                $"NetBoxPlus hesabının şifresi {when} (UTC) tarihinde değiştirildi ve tüm cihazlarda oturumun kapatıldı.",
                "Bunu sen yaptıysan başka bir şey yapmana gerek yok.",
                "Sen yapmadıysan hemen şifreni sıfırla.",
            ],
            ("Şifremi sıfırla", reset));
    }

    /// <param name="videoQuality">The plan's quality as the billing service names it: <c>Sd</c>, <c>Hd</c> or <c>Uhd</c>.</param>
    /// <param name="nextPayment">When the next payment is due, if known; shown as a UTC date.</param>
    public static EmailContent SubscriptionStarted(string videoQuality, int maxScreens, DateTimeOffset? nextPayment, Uri home)
    {
        ArgumentNullException.ThrowIfNull(home);
        var paragraphs = new List<string>
        {
            "Ödemen alındı ve NetBoxPlus aboneliğin başladı.",
            $"Planın: {QualityName(videoQuality)} görüntü, aynı anda {maxScreens} ekran.",
        };
        if (nextPayment is { } due)
        {
            paragraphs.Add($"Sonraki ödeme {Date(due)} tarihinde alınacak.");
        }

        return Render("Aboneliğin başladı", paragraphs, ("İzlemeye başla", home));
    }

    public static EmailContent SubscriptionResumed(Uri home)
    {
        ArgumentNullException.ThrowIfNull(home);
        return Render(
            "Ödemen alındı",
            ["Bekleyen ödemen alındı ve aboneliğin yeniden aktif.", "İzlemeye kaldığın yerden devam edebilirsin."],
            ("İzlemeye devam et", home));
    }

    /// <param name="amountMinor">The amount in the currency's smallest unit (kuruş for TRY).</param>
    public static EmailContent PaymentFailed(long amountMinor, string currency, Uri account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return Render(
            "Ödemen alınamadı",
            [
                $"Aboneliğinin {Money(amountMinor, currency)} tutarındaki ödemesi alınamadı.",
                "Ödeme yapılana kadar izleme duraklatıldı. Ödeme birkaç gün içinde yeniden denenecek; ödeme bilgilerini güncellersen hemen alınır.",
            ],
            ("Ödeme bilgilerini güncelle", account));
    }

    /// <param name="endedAt">Shown as a UTC date.</param>
    public static EmailContent SubscriptionEnded(DateTimeOffset endedAt, Uri plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        return Render(
            "Aboneliğin sona erdi",
            [
                $"NetBoxPlus aboneliğin {Date(endedAt)} tarihinde sona erdi.",
                "Seni yeniden görmek isteriz; dilediğin zaman yeni bir plan seçebilirsin.",
            ],
            ("Planları gör", plans));
    }

    private static string QualityName(string videoQuality) => videoQuality switch
    {
        "Sd" => "SD",
        "Hd" => "HD",
        "Uhd" => "Ultra HD",
        _ => videoQuality,
    };

    private static string Date(DateTimeOffset value) => value.ToUniversalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Turkish style, two decimals (every supported currency has them): <c>1.249,99 TL</c>.</summary>
    private static string Money(long amountMinor, string currency)
    {
        var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
        format.NumberDecimalSeparator = ",";
        format.NumberGroupSeparator = ".";
        var amount = (amountMinor / 100m).ToString("N2", format);
        return $"{amount} {(currency == "TRY" ? "TL" : currency)}";
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
